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
        // Konstanten
        private const string ProgramVersionKey = "ProgramVersion";
        private const string RepoName = "LS25-GitHub-Mod-Downloader";
        private const string OutputFolderName = "Output";
        private const int FileCompareBufferSize = 8192;
        private const int UpdateWaitSeconds = 3;

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
                    Log.Information("Kein Update gefunden.");
                    Console.WriteLine("Kein Update gefunden.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(release.TagName) || !Version.TryParse(release.TagName.TrimStart('v'), out Version? latestVersion))
                {
                    Log.Warning("Ungültige Versionsnummer im Release: {TagName}", release.TagName ?? "null");
                    return;
                }

                var versions = await _versionManager.GetCurrentVersionsAsync();
                Version currentVersion = versions.ContainsKey(ProgramVersionKey)
                    ? versions[ProgramVersionKey]
                    : new Version(0, 0, 0);

                if (currentVersion < latestVersion)
                {
                    Log.Information("Update verfügbar: {CurrentVersion} -> {LatestVersion}", currentVersion, latestVersion);
                    DisplayUpdatePrompt(currentVersion, latestVersion);

                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Enter)
                    {
                        await InitiateUpdateAsync(release, latestVersion, versions);
                    }
                    else
                    {
                        Log.Information("Update vom Benutzer übersprungen.");
                        Console.WriteLine("Update übersprungen. Starte Anwendung wie gewohnt...");
                    }
                }
                else
                {
                    Log.Information("Aktuellste Version ({Version}) bereits installiert.", currentVersion);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fehler beim Überprüfen der Software-Version.");
            }
        }

        /// <summary>
        /// Zeigt die Update-Aufforderung an.
        /// </summary>
        private void DisplayUpdatePrompt(Version currentVersion, Version latestVersion)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Es ist ein Update verfügbar!");
            Console.WriteLine($"Aktuelle Version: {currentVersion}");
            Console.WriteLine($"Neue Version: {latestVersion}");
            Console.WriteLine("Drücke Enter, um das Update durchzuführen oder eine beliebige andere Taste, um es zu überspringen...");
            Console.ResetColor();
        }

        /// <summary>
        /// Initiiert den Update-Prozess mit Admin-Rechte-Prüfung.
        /// </summary>
        private async Task InitiateUpdateAsync(GitHubRelease release, Version latestVersion, Dictionary<string, Version> versions)
        {
            Log.Information("Update wird durchgeführt...");
            Console.WriteLine("Update wird durchgeführt...");

            if (!UserInteraction.IsUserAdministrator())
            {
                Log.Warning("Keine Administratorrechte. Starte Programm mit Admin-Rechten neu.");
                Console.WriteLine("Das Update benötigt Administratorrechte. Starte Programm neu mit Admin-Rechten...");
                UserInteraction.RestartAsAdmin();
                return;
            }

            await PerformUpdateAsync(release, latestVersion, versions);
        }

        private async Task PerformUpdateAsync(GitHubRelease release, Version latestVersion, Dictionary<string, Version> versions)
        {
            string tempZipPath = Path.Combine(Path.GetTempPath(), $"{RepoName}_update.zip");
            string tempExtractPath = Path.Combine(Path.GetTempPath(), $"{RepoName}_update");
            string batchFilePath = Path.Combine(Path.GetTempPath(), "update.bat");

            try
            {
                // Download Update
                Log.Information("Update wird heruntergeladen...");
                await DownloadUpdateAsync(release, tempZipPath);

                // Extrahiere Update
                Log.Information("Update wird entpackt...");
                await ExtractUpdateAsync(tempZipPath, tempExtractPath);

                // Erstelle und starte Batch-Updater
                Log.Information("Erstelle Update-Batch-Datei...");
                string? currentExecutablePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(currentExecutablePath))
                {
                    throw new InvalidOperationException("Konnte Pfad zur ausführbaren Datei nicht ermitteln.");
                }

                string? currentDirectory = Path.GetDirectoryName(currentExecutablePath);
                if (string.IsNullOrWhiteSpace(currentDirectory))
                {
                    throw new InvalidOperationException("Konnte Verzeichnis der ausführbaren Datei nicht ermitteln.");
                }

                CreateUpdateBatchFile(batchFilePath, tempExtractPath, currentDirectory, currentExecutablePath);

                // Speichere neue Version VOR dem Neustart
                versions[ProgramVersionKey] = latestVersion;
                await SaveCurrentVersionsAsync(versions);
                Log.Information("Neue Version {Version} gespeichert.", latestVersion);

                // Starte Batch und beende Anwendung
                Log.Information("Starte Update-Batch und beende Anwendung...");
                Console.WriteLine("Update wird installiert. Die Anwendung wird neu gestartet...");

                StartUpdateBatchAndExit(batchFilePath);
            }
            catch (HttpRequestException ex)
            {
                Log.Error(ex, "Netzwerkfehler beim Herunterladen des Updates.");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Netzwerkfehler beim Herunterladen des Updates: {ex.Message}");
                Console.ResetColor();
                CleanupTempFiles(new List<string> { tempZipPath, tempExtractPath, batchFilePath });
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Error(ex, "Zugriffsfehler beim Durchführen des Updates. Möglicherweise fehlen Administratorrechte.");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Zugriffsfehler: {ex.Message}. Stellen Sie sicher, dass das Programm mit Administratorrechten läuft.");
                Console.ResetColor();
                CleanupTempFiles(new List<string> { tempZipPath, tempExtractPath, batchFilePath });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fehler beim Durchführen des Updates.");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Fehler beim Durchführen des Updates: {ex.Message}");
                Console.ResetColor();
                CleanupTempFiles(new List<string> { tempZipPath, tempExtractPath, batchFilePath });
            }
        }

        /// <summary>
        /// Lädt das Update von GitHub herunter.
        /// </summary>
        private async Task DownloadUpdateAsync(GitHubRelease release, string tempZipPath)
        {
            string? downloadUrl = release.Assets?.FirstOrDefault()?.BrowserDownloadUrl;
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                throw new InvalidOperationException("Download-URL nicht gefunden.");
            }

            Console.WriteLine("Update wird heruntergeladen...");
            Log.Information("Lade Update herunter von: {Url}", downloadUrl);

            using (var updateResponse = await _client.GetAsync(downloadUrl))
            {
                updateResponse.EnsureSuccessStatusCode();
                await using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await updateResponse.Content.CopyToAsync(fs);
                }
            }

            Log.Information("Update heruntergeladen: {Size} Bytes", new FileInfo(tempZipPath).Length);
        }

        /// <summary>
        /// Entpackt das Update und überspringt den Output-Ordner.
        /// </summary>
        private Task ExtractUpdateAsync(string tempZipPath, string tempExtractPath)
        {
            if (Directory.Exists(tempExtractPath))
            {
                Directory.Delete(tempExtractPath, true);
            }
            Directory.CreateDirectory(tempExtractPath);

            return Task.Run(() =>
            {
                using (ZipArchive archive = ZipFile.OpenRead(tempZipPath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        // Überspringe Output-Ordner (mit beiden Pfad-Trennzeichen)
                        if (IsInOutputFolder(entry.FullName))
                        {
                            Log.Debug("Überspringe Datei im Output-Ordner: {FileName}", entry.FullName);
                            continue;
                        }

                        string destinationPath = Path.Combine(tempExtractPath, entry.FullName);

                        // Verzeichnis erstellen
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destinationPath);
                        }
                        else
                        {
                            string? directoryPath = Path.GetDirectoryName(destinationPath);
                            if (!string.IsNullOrEmpty(directoryPath))
                            {
                                Directory.CreateDirectory(directoryPath);
                            }
                            entry.ExtractToFile(destinationPath, overwrite: true);
                        }
                    }
                }

                Log.Information("Update entpackt nach: {Path}", tempExtractPath);
            });
        }

        /// <summary>
        /// Prüft, ob eine Datei im Output-Ordner liegt.
        /// </summary>
        private bool IsInOutputFolder(string path)
        {
            return path.StartsWith($"{OutputFolderName}/", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith($"{OutputFolderName}\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Erstellt die Batch-Datei für das Update.
        /// </summary>
        private void CreateUpdateBatchFile(string batchFilePath, string tempExtractPath, string currentDirectory, string currentExecutablePath)
        {
            string processId = Process.GetCurrentProcess().Id.ToString();
            
            // Batch wartet auf Prozess-Beendigung, kopiert Dateien, startet neu
            string batchContent = $@"@echo off
echo Warte auf Beendigung der Anwendung (PID: {processId})...
:WAIT
tasklist /FI ""PID eq {processId}"" 2>NUL | find ""{processId}"" >NUL
if ""%%ERRORLEVEL%"" == ""0"" (
    timeout /t 1 /nobreak >nul
    goto WAIT
)
echo Anwendung beendet. Installiere Update...
timeout /t {UpdateWaitSeconds} /nobreak >nul
xcopy /Y /E /I /Q ""{tempExtractPath}\*"" ""{currentDirectory}\""
if errorlevel 1 (
    echo Fehler beim Kopieren der Dateien!
    pause
    exit /b 1
)
echo Update installiert. Starte Anwendung neu...
start """" ""{currentExecutablePath}""
echo Räume temporäre Dateien auf...
rmdir /S /Q ""{tempExtractPath}""
del ""{tempExtractPath.Replace("_update", "_update.zip")}""
del ""%~f0""";

            File.WriteAllText(batchFilePath, batchContent);
            Log.Information("Update-Batch-Datei erstellt: {Path}", batchFilePath);
        }

        /// <summary>
        /// Startet die Update-Batch-Datei und beendet die Anwendung.
        /// </summary>
        private void StartUpdateBatchAndExit(string batchFilePath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = batchFilePath,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = false
                };

                Process.Start(startInfo);
                Log.Information("Update-Batch gestartet. Beende Anwendung.");

                // Kurz warten, damit Batch starten kann
                Task.Delay(500).Wait();
                
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fehler beim Starten der Update-Batch-Datei.");
                throw;
            }
        }

        /// <summary>
        /// Vergleicht zwei Dateien anhand ihrer Größe und Inhalte.
        /// </summary>
        private bool CompareFiles(string file1, string file2)
        {
            try
            {
                if (!File.Exists(file1) || !File.Exists(file2))
                {
                    return false;
                }

                FileInfo fi1 = new FileInfo(file1);
                FileInfo fi2 = new FileInfo(file2);
                
                if (fi1.Length != fi2.Length)
                {
                    return false;
                }

                using (FileStream fs1 = File.OpenRead(file1))
                using (FileStream fs2 = File.OpenRead(file2))
                {
                    byte[] buffer1 = new byte[FileCompareBufferSize];
                    byte[] buffer2 = new byte[FileCompareBufferSize];
                    int bytesRead1, bytesRead2;
                    
                    while ((bytesRead1 = fs1.Read(buffer1, 0, FileCompareBufferSize)) > 0)
                    {
                        bytesRead2 = fs2.Read(buffer2, 0, FileCompareBufferSize);
                        if (bytesRead1 != bytesRead2)
                        {
                            return false;
                        }
                        
                        if (!buffer1.AsSpan(0, bytesRead1).SequenceEqual(buffer2.AsSpan(0, bytesRead2)))
                        {
                            return false;
                        }
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Fehler beim Vergleichen der Dateien {File1} und {File2}", file1, file2);
                return false;
            }
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
                        Log.Debug("Temporäre Datei gelöscht: {Path}", path);
                    }
                    else if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                        Log.Debug("Temporäres Verzeichnis gelöscht: {Path}", path);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Fehler beim Löschen temporärer Datei/Ordner: {Path}", path);
                }
            }
        }
    }
}
