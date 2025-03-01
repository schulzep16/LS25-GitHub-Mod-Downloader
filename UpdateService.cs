using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;

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
                Version currentVersion = versions.ContainsKey("ProgramVersion") ? versions["ProgramVersion"] : new Version(0, 0, 0);
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
            string repoName = _config.GitHubRepo;
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

                // Signaturprüfung
                var sigAsset = release.Assets.FirstOrDefault(a => a.BrowserDownloadUrl.EndsWith(".sig", StringComparison.OrdinalIgnoreCase));
                if (sigAsset != null)
                {
                    string sigFilePath = Path.Combine(Path.GetTempPath(), $"{repoName}_update.sig");
                    using (var sigResponse = await _client.GetAsync(sigAsset.BrowserDownloadUrl))
                    {
                        sigResponse.EnsureSuccessStatusCode();
                        using (var fs = new FileStream(sigFilePath, FileMode.Create))
                        {
                            await sigResponse.Content.CopyToAsync(fs);
                        }
                    }
                    if (!SecurityHelper.VerifyFileSignature(tempZipPath, sigFilePath))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Die Signaturprüfung des Updates ist fehlgeschlagen.");
                        Console.ResetColor();
                        if (File.Exists(sigFilePath)) File.Delete(sigFilePath);
                        if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
                        return;
                    }
                    File.Delete(sigFilePath);
                }
                else
                {
                    Console.WriteLine("Warnung: Keine Signatur gefunden. Update wird ohne Signaturprüfung durchgeführt.");
                }

                // Extrahiere das Update (ohne den "Output"-Ordner)
                if (Directory.Exists(tempExtractPath))
                {
                    Directory.Delete(tempExtractPath, true);
                }
                Directory.CreateDirectory(tempExtractPath);
                using (ZipArchive archive = ZipFile.OpenRead(tempZipPath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
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
                            entry.ExtractToFile(destinationPath, true);
                        }
                    }
                }

                // Erstelle Batch-Datei zum Update
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
                var batchProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = batchFilePath,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                });
                batchProcess.WaitForExit();
                bool updateSuccessful = Directory.GetFiles(tempExtractPath, "*", SearchOption.AllDirectories)
                    .All(tempFile =>
                    {
                        string relativePath = Path.GetRelativePath(tempExtractPath, tempFile);
                        string targetFile = Path.Combine(currentDirectory, relativePath);
                        return File.Exists(targetFile) && FileHelper.CompareFiles(tempFile, targetFile);
                    });
                if (updateSuccessful)
                {
                    versions["ProgramVersion"] = latestVersion;
                    await _versionManager.SaveCurrentVersionsAsync(versions);
                    Console.WriteLine("Update erfolgreich durchgeführt. Starte Anwendung neu...");
                    FileHelper.CleanupTempFiles(new List<string> { tempZipPath, tempExtractPath, batchFilePath });
                    Environment.Exit(0);
                }
                else
                {
                    Console.WriteLine("Fehler: Nicht alle Dateien wurden erfolgreich aktualisiert.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fehler beim Durchführen des Updates.");
            }
            finally
            {
                FileHelper.CleanupTempFiles(new List<string> { tempZipPath, tempExtractPath, batchFilePath });
            }
        }
    }
}
