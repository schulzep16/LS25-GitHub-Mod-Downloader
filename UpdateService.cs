using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace LS25ModDownloader
{
    public class UpdateService
    {
        private readonly Config _config;
        private readonly HttpClient _client;
        private readonly VersionManager _versionManager;

        public UpdateService(Config config, HttpClient client, VersionManager versionManager)
        {
            _config = config;
            _client = client;
            _versionManager = versionManager;
        }

        public async Task CheckForSoftwareUpdateAsync()
        {
            try
            {
                var release = await GitHubService.GetLatestReleaseAsync(_client, _config.GitHubUser, _config.GitHubRepo);
                if (release == null)
                {
                    Console.WriteLine("Kein Update gefunden.");
                    return;
                }
                if (!Version.TryParse(release.TagName.TrimStart('v'), out Version latestVersion))
                {
                    Log.Warning("Ungültige Versionsnummer im Release: {TagName}", release.TagName);
                    return;
                }
                var versions = await _versionManager.GetCurrentVersionsAsync();
                // Wir erwarten hier ein Dictionary<string, Version>
                Version currentVersion = versions.ContainsKey("ProgramVersion")
                    ? versions["ProgramVersion"]
                    : new Version(0, 0, 0);
                if (currentVersion < latestVersion)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Es ist ein Update verfügbar!");
                    Console.WriteLine($"Aktuelle Version: {currentVersion}");
                    Console.WriteLine($"Neue Version: {latestVersion}");
                    Console.WriteLine("Drücke Enter, um das Update durchzuführen oder eine beliebige andere Taste, um es zu überspringen...");
                    Console.ResetColor();

                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Enter)
                    {
                        Console.WriteLine("Update wird durchgeführt...");
                        if (!UserInteraction.IsUserAdministrator())
                        {
                            Console.WriteLine("Das Update benötigt Administratorrechte. Starte Programm neu mit Admin-Rechten...");
                            UserInteraction.RestartAsAdmin();
                            return;
                        }
                        await PerformUpdateAsync(release, latestVersion, versions);
                    }
                    else
                    {
                        Console.WriteLine("Update übersprungen. Starte Anwendung wie gewohnt...");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fehler beim Überprüfen der Software-Version.");
            }
        }

        private async Task PerformUpdateAsync(GitHubRelease release, Version latestVersion, Dictionary<string, Version> versions)
        {
            string repoName = "LS25-GitHub-Mod-Downloader";
            string tempZipPath = Path.Combine(Path.GetTempPath(), $"{repoName}_update.zip");
            string tempExtractPath = Path.Combine(Path.GetTempPath(), $"{repoName}_update");
            string batchFilePath = Path.Combine(Path.GetTempPath(), "update.bat");

            try
            {
                Console.WriteLine("Update wird heruntergeladen...");

                string downloadUrl = release.Assets.FirstOrDefault()?.BrowserDownloadUrl;
                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    Console.WriteLine("Download-URL nicht gefunden.");
                    return;
                }

                using (var updateResponse = await _client.GetAsync(downloadUrl))
                {
                    updateResponse.EnsureSuccessStatusCode();
                    using (var fs = new FileStream(tempZipPath, FileMode.Create))
                    {
                        await updateResponse.Content.CopyToAsync(fs);
                    }
                }

                // Entpacke das Update in einen temporären Ordner, überspringe dabei den "Output"-Ordner
                if (Directory.Exists(tempExtractPath))
                {
                    Directory.Delete(tempExtractPath, true);
                }
                Directory.CreateDirectory(tempExtractPath);

                using (ZipArchive archive = ZipFile.OpenRead(tempZipPath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        // Überspringe alle Einträge, die im "Output"-Ordner liegen
                        if (entry.FullName.StartsWith("Output/", StringComparison.OrdinalIgnoreCase) ||
                            entry.FullName.StartsWith("Output\\", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string destinationPath = Path.Combine(tempExtractPath, entry.FullName);
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destinationPath);
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                            entry.ExtractToFile(destinationPath, overwrite: true);
                        }
                    }
                }

                // Erstelle eine Batch-Datei, die das Update anwendet
                string currentExecutablePath = Process.GetCurrentProcess().MainModule.FileName;
                string currentDirectory = Path.GetDirectoryName(currentExecutablePath);
                string batchContent = $@"@echo off
echo Warte auf Beendigung der Anwendung...
ping 127.0.0.1 -n 5 >nul
echo Update wird installiert...
xcopy /Y /E ""{tempExtractPath}\*"" ""{currentDirectory}\""
echo Update installiert.
start """" ""{currentExecutablePath}""
del ""%~f0""";
                File.WriteAllText(batchFilePath, batchContent);

                // Starte die Batch-Datei mit Admin-Rechten und warte auf deren Beendigung.
                var batchProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = batchFilePath,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                });
                batchProcess.WaitForExit();

                // Überprüfe, ob alle Dateien erfolgreich aktualisiert wurden.
                string currentExecutableDir = Path.GetDirectoryName(currentExecutablePath);
                bool updateSuccessful = Directory.GetFiles(tempExtractPath, "*", SearchOption.AllDirectories)
                    .All(tempFile =>
                    {
                        string relativePath = Path.GetRelativePath(tempExtractPath, tempFile);
                        string targetFile = Path.Combine(currentExecutableDir, relativePath);
                        return File.Exists(targetFile) && CompareFiles(tempFile, targetFile);
                    });

                if (updateSuccessful)
                {
                    versions["ProgramVersion"] = latestVersion;
                    await SaveCurrentVersionsAsync(versions);
                    Console.WriteLine("Update erfolgreich durchgeführt. Starte Anwendung neu...");
                    CleanupTempFiles(new List<string> { tempZipPath, tempExtractPath, batchFilePath });
                    Environment.Exit(0);
                }
                else
                {
                    Console.WriteLine("Fehler: Nicht alle Dateien wurden erfolgreich aktualisiert.");
                }
            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Fehler beim Durchführen des Updates: {e.Message}");
            }
            finally
            {
                CleanupTempFiles(new List<string> { tempZipPath, tempExtractPath, batchFilePath });
            }
        }

        /// <summary>
        /// Vergleicht zwei Dateien anhand ihrer Größe und Inhalte.
        /// </summary>
        private bool CompareFiles(string file1, string file2)
        {
            FileInfo fi1 = new FileInfo(file1);
            FileInfo fi2 = new FileInfo(file2);
            if (fi1.Length != fi2.Length)
                return false;

            const int bufferSize = 8192;
            using (FileStream fs1 = File.OpenRead(file1))
            using (FileStream fs2 = File.OpenRead(file2))
            {
                byte[] buffer1 = new byte[bufferSize];
                byte[] buffer2 = new byte[bufferSize];
                int bytesRead1, bytesRead2;
                while ((bytesRead1 = fs1.Read(buffer1, 0, bufferSize)) > 0)
                {
                    bytesRead2 = fs2.Read(buffer2, 0, bufferSize);
                    if (bytesRead1 != bytesRead2)
                        return false;
                    for (int i = 0; i < bytesRead1; i++)
                    {
                        if (buffer1[i] != buffer2[i])
                            return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Speichert die aktuellen Versionsdaten. Delegiert an den VersionManager.
        /// </summary>
        private async Task SaveCurrentVersionsAsync(Dictionary<string, Version> versions)
        {
            await _versionManager.SaveCurrentVersionsAsync(versions);
        }

        /// <summary>
        /// Löscht temporäre Dateien bzw. Verzeichnisse.
        /// </summary>
        private void CleanupTempFiles(List<string> paths)
        {
            foreach (var path in paths)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    else if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Fehler beim Löschen temporärer Datei/Ordner {path}: {ex.Message}");
                }
            }
        }
    }
}
