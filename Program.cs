using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Xml.Linq;

#region GitHub Models und Service

public class GitHubProject
{
    public string Username { get; set; }
    public string Repo { get; set; }
}

public class GitHubRelease
{
    [JsonProperty("tag_name")]
    public string TagName { get; set; }

    [JsonProperty("assets")]
    public List<GitHubAsset> Assets { get; set; }
}

public class GitHubAsset
{
    [JsonProperty("browser_download_url")]
    public string BrowserDownloadUrl { get; set; }
}

public static class GitHubService
{
    private static readonly HttpClient client = new HttpClient();

    static GitHubService()
    {
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            client.DefaultRequestHeaders.UserAgent.ParseAdd("request");
    }

    public static async Task<GitHubRelease> GetLatestReleaseAsync(string username, string repoName)
    {
        try
        {
            string url = $"https://api.github.com/repos/{username}/{repoName}/releases/latest";
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            string content = await response.Content.ReadAsStringAsync();
            var release = JsonConvert.DeserializeObject<GitHubRelease>(content);
            return release;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Netzwerkfehler beim Abrufen des Releases für {username}/{repoName}: {ex.Message}");
            return null;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Fehler beim Deserialisieren des Releases für {username}/{repoName}: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unbekannter Fehler beim Abrufen des Releases für {username}/{repoName}: {ex.Message}");
            return null;
        }
    }

    public static async Task<bool> ValidateProjectAsync(string username, string repoName)
    {
        var release = await GetLatestReleaseAsync(username, repoName);
        return release != null;
    }
}

#endregion

class Program
{
    // Aktuelle Version der Software
    private const string DefaultVersion = "1.0.0"; // Standard-Version, falls keine Version in current_version.txt gefunden wird

    private static readonly string ModFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "My Games", "FarmingSimulator2025", "mods");
    private static readonly string CurrentVersionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "current_version.txt");
    private static readonly string ProjectsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "projects.json");

    // Liste der Projekte, die aus der JSON-Datei geladen wird
    private static List<GitHubProject> Projects = new List<GitHubProject>();

    private static readonly HttpClient client = new HttpClient();

    static async Task Main(string[] args)
    {
        // Sicherstellen, dass der Mod-Ordner existiert
        if (!Directory.Exists(ModFolder))
        {
            Directory.CreateDirectory(ModFolder);
        }

        // Versionsnummer des Programms in current_version.txt prüfen und ggf. hinzufügen
        await EnsureProgramVersionInFileAsync();

        // Aktuelle Version aus der Datei lesen und anzeigen
        var versions = await GetCurrentVersionsAsync();
        string currentVersion = versions.ContainsKey("ProgramVersion") ? versions["ProgramVersion"] : DefaultVersion;
        Console.WriteLine($"Programm Version: {currentVersion}");
        Console.WriteLine();

        // Update prüfen – falls verfügbar, wird der Nutzer informiert und muss per Eingabe bestätigen
        await CheckForSoftwareUpdateAsync();

        // Projekte aus der JSON-Datei laden (oder Standardwerte setzen)
        await LoadProjectsAsync();

        // Integrierte Mods anzeigen
        DisplayIntegratedMods();

        // Dem Nutzer die Möglichkeit geben, Projekte zu verwalten (Hinzufügen/Löschen)
        await ManageProjectsAsync();

        Console.WriteLine("Starte Überprüfung der Mods...");
        await InitializeCurrentVersionsAsync();
        versions = await GetCurrentVersionsAsync();

        // Sicherstellen, dass der User-Agent gesetzt ist
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            client.DefaultRequestHeaders.UserAgent.ParseAdd("request");

        foreach (var project in Projects)
        {
            await CheckAndUpdateModAsync(project, versions);
        }

        await SaveCurrentVersionsAsync(versions);
        Console.WriteLine("Überprüfung abgeschlossen.");
        Console.WriteLine();

        // Nach erfolgreichem Update den Nutzer fragen, ob der Farming Simulator 2025 gestartet werden soll.
        Console.Write("Möchten Sie den Farming Simulator 2025 starten? (J/N): ");
        var startGameInput = Console.ReadLine();
        if (!string.IsNullOrEmpty(startGameInput) && startGameInput.Trim().ToUpper().StartsWith("J"))
        {
            StartFarmingSimulator();
        }

        Console.WriteLine("Drücke Enter, um das Programm zu beenden.");
        Console.ReadLine();
    }

    static async Task EnsureProgramVersionInFileAsync()
    {
        var versions = await GetCurrentVersionsAsync();
        if (!versions.ContainsKey("ProgramVersion"))
        {
            // Keine Version gefunden, Update auslösen
            await CheckForSoftwareUpdateAsync();
            versions = await GetCurrentVersionsAsync();
        }
    }

    static async Task LoadProjectsAsync()
    {
        if (File.Exists(ProjectsFile))
        {
            var json = await File.ReadAllTextAsync(ProjectsFile);
            Projects = JsonConvert.DeserializeObject<List<GitHubProject>>(json);
        }
        else
        {
            // Suche im Mod-Ordner nach bekannten Mods
            var foundMods = ScanModFolderForKnownMods();
            foreach (var mod in foundMods)
            {
                Console.Write($"Mod {mod.Repo} wurde im Mod-Ordner gefunden. Möchten Sie ihn einbinden? (J/N): ");
                var answer = Console.ReadLine();
                if (!string.IsNullOrEmpty(answer) && answer.Trim().ToUpper().StartsWith("J"))
                {
                    Projects.Add(mod);
                }
            }
            // Falls keine Mods eingebunden wurden, Frage nach den standardmäßig eingebundenen Mods
            if (Projects.Count == 0)
            {
                Console.Write("Es wurden keine bekannten Mods eingebunden. Möchten Sie die standardmäßig eingebundenen Mods hinzufügen? (J/N): ");
                var answer = Console.ReadLine();
                if (!string.IsNullOrEmpty(answer) && answer.Trim().ToUpper().StartsWith("J"))
                {
                    Projects = new List<GitHubProject>
                    {
                        new GitHubProject { Username = "loki79uk", Repo = "FS25_UniversalAutoload" },
                        new GitHubProject { Username = "Courseplay", Repo = "Courseplay_FS25" },
                        new GitHubProject { Username = "Stephan-S", Repo = "FS25_AutoDrive" }
                    };
                }
            }
            await SaveProjectsAsync();
        }
    }

    static async Task SaveProjectsAsync()
    {
        var json = JsonConvert.SerializeObject(Projects, Formatting.Indented);
        await File.WriteAllTextAsync(ProjectsFile, json);
    }

    static void DisplayIntegratedMods()
    {
        Console.WriteLine("Aktuell berücksichtigte Mods:");
        if (Projects.Count == 0)
        {
            Console.WriteLine("Keine Mods integriert.");
        }
        else
        {
            foreach (var project in Projects)
            {
                Console.WriteLine($"- {project.Username}/{project.Repo}");
            }
        }
        Console.WriteLine();
    }

    static async Task ManageProjectsAsync()
    {
        Console.WriteLine("=== Projektverwaltung ===");
        Console.WriteLine("1. Projekt hinzufügen");
        Console.WriteLine("2. Projekt löschen");
        Console.WriteLine("3. Mit Update fortfahren");
        Console.Write("Wähle eine Option (oder Enter drücken zum Fortfahren): ");
        var choice = Console.ReadLine();
        switch (choice)
        {
            case "1":
                Console.Write("GitHub Username: ");
                var username = Console.ReadLine();
                Console.Write("Repository Name: ");
                var repo = Console.ReadLine();

                bool valid = await GitHubService.ValidateProjectAsync(username, repo);
                if (!valid)
                {
                    Console.WriteLine("Projekt nicht gefunden oder es existieren keine Releases.");
                    break;
                }

                Projects.Add(new GitHubProject { Username = username, Repo = repo });
                await SaveProjectsAsync();
                Console.WriteLine("Projekt hinzugefügt.");
                break;

            case "2":
                if (Projects.Count == 0)
                {
                    Console.WriteLine("Keine Projekte vorhanden, die gelöscht werden können.");
                    break;
                }
                Console.WriteLine("Verfügbare Projekte:");
                for (int i = 0; i < Projects.Count; i++)
                {
                    Console.WriteLine($"{i + 1}. {Projects[i].Username}/{Projects[i].Repo}");
                }
                Console.Write("Nummer des zu löschenden Projekts: ");
                if (int.TryParse(Console.ReadLine(), out int index) && index > 0 && index <= Projects.Count)
                {
                    // Projekt aus der Liste entfernen
                    GitHubProject removedProject = Projects[index - 1];
                    Projects.RemoveAt(index - 1);
                    await SaveProjectsAsync();
                    Console.WriteLine("Projekt gelöscht.");

                    // Entferne den Eintrag aus der current_version.txt
                    var versions = await GetCurrentVersionsAsync();
                    if (versions.Remove(removedProject.Repo))
                    {
                        await SaveCurrentVersionsAsync(versions);
                        Console.WriteLine("Zugehöriger Eintrag in current_version.txt entfernt.");
                    }

                    // Frage, ob auch der Mod aus dem Mod-Ordner gelöscht werden soll
                    Console.Write("Soll auch der Mod aus dem Mod-Ordner gelöscht werden? (J/N): ");
                    string modDeleteChoice = Console.ReadLine();
                    if (modDeleteChoice != null && modDeleteChoice.Trim().ToUpper().StartsWith("J"))
                    {
                        string modZipPath = Path.Combine(ModFolder, $"{removedProject.Repo}.zip");
                        if (File.Exists(modZipPath))
                        {
                            File.Delete(modZipPath);
                            Console.WriteLine("Mod-Datei wurde gelöscht.");
                        }
                        else
                        {
                            Console.WriteLine("Keine Mod-Datei im Mod-Ordner gefunden.");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("Ungültige Eingabe.");
                }
                break;
            default:
                Console.WriteLine("Fortfahren mit Update.");
                break;
        }
        Console.WriteLine();
    }

    static async Task CheckAndUpdateModAsync(GitHubProject project, Dictionary<string, string> currentVersions)
    {
        string username = project.Username, repoName = project.Repo;
        Console.WriteLine($"Prüfe Updates für {repoName}...");

        try
        {
            var release = await GitHubService.GetLatestReleaseAsync(username, repoName);
            if (release == null)
            {
                Console.WriteLine($"Kein Release gefunden für {repoName}.");
                return;
            }
            string latestVersion = release.TagName.TrimStart('v');
            string currentVersion = currentVersions.ContainsKey(repoName) ? currentVersions[repoName] : "Not Installed";
            string modZipPath = Path.Combine(ModFolder, $"{repoName}.zip");

            if (!File.Exists(modZipPath))
            {
                currentVersion = "Not Installed";
                currentVersions[repoName] = "Not Installed";
            }

            if (currentVersion != latestVersion)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Neue Version gefunden für {repoName}: {latestVersion} (Aktuelle Version: {currentVersion})");

                // Annahme: Erster Asset wird verwendet
                string downloadUrl = release.Assets.FirstOrDefault()?.BrowserDownloadUrl;
                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    Console.WriteLine("Download-URL nicht gefunden.");
                    return;
                }

                var downloadResponse = await client.GetAsync(downloadUrl);
                if (downloadResponse.IsSuccessStatusCode)
                {
                    var tempZipPath = Path.Combine(ModFolder, $"{repoName}.zip");
                    using (var fs = new FileStream(tempZipPath, FileMode.Create))
                    {
                        await downloadResponse.Content.CopyToAsync(fs);
                    }

                    string extractedVersion = await ExtractVersionFromZipAsync(tempZipPath);

                    if (extractedVersion == latestVersion)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"{repoName} erfolgreich aktualisiert auf Version {latestVersion}");
                        currentVersions[repoName] = latestVersion;
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        File.Delete(tempZipPath);
                        Console.WriteLine($"Fehler: Heruntergeladene Version von {repoName} stimmt nicht mit der erwarteten Version überein.");
                    }
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"{repoName} ist bereits auf dem neuesten Stand.");
            }
        }
        catch (HttpRequestException e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Netzwerkfehler beim Überprüfen von {repoName}: {e.Message}");
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Fehler beim Überprüfen von {repoName}: {e.Message}");
        }
        finally
        {
            Console.ResetColor();
        }
    }

    static async Task<Dictionary<string, string>> GetCurrentVersionsAsync()
    {
        var versions = new Dictionary<string, string>();
        if (File.Exists(CurrentVersionFile))
        {
            try
            {
                foreach (var line in await File.ReadAllLinesAsync(CurrentVersionFile))
                {
                    var parts = line.Split(':');
                    if (parts.Length == 2)
                    {
                        versions[parts[0]] = parts[1];
                    }
                }
            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Fehler beim Lesen der {CurrentVersionFile}: {e.Message}");
                Console.ResetColor();
            }
        }
        return versions;
    }

    static async Task SaveCurrentVersionsAsync(Dictionary<string, string> versions)
    {
        try
        {
            using (var sw = new StreamWriter(CurrentVersionFile))
            {
                foreach (var kvp in versions)
                {
                    await sw.WriteLineAsync($"{kvp.Key}:{kvp.Value}");
                }
            }
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Fehler beim Speichern der {CurrentVersionFile}: {e.Message}");
            Console.ResetColor();
        }
    }

    static async Task<string> ExtractVersionFromZipAsync(string zipPath)
    {
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var entry = zip.GetEntry("modDesc.xml");
            if (entry != null)
            {
                using (var stream = entry.Open())
                {
                    var doc = XDocument.Load(stream);
                    return doc.Root?.Element("version")?.Value ?? "unknown";
                }
            }
            else
            {
                return "unknown";
            }
        }
    }

    static async Task InitializeCurrentVersionsAsync()
    {
        var versions = await GetCurrentVersionsAsync();
        bool updated = false;

        // Entferne nicht mehr benötigte Einträge
        var keysToRemove = new List<string>();
        foreach (var key in versions.Keys)
        {
            if (key != "ProgramVersion" && !Projects.Exists(p => p.Repo == key))
            {
                keysToRemove.Add(key);
            }
        }
        foreach (var key in keysToRemove)
        {
            versions.Remove(key);
            updated = true;
        }

        // Füge fehlende Einträge hinzu
        foreach (var project in Projects)
        {
            string repoName = project.Repo;
            if (!versions.ContainsKey(repoName))
            {
                versions[repoName] = "Not Installed";
                updated = true;
            }
        }

        if (updated)
        {
            await SaveCurrentVersionsAsync(versions);
        }
    }

    /// <summary>
    /// Prüft beim Programmstart, ob ein Software-Update verfügbar ist.  
    /// Falls ja, wird der Nutzer informiert und muss das Update per Eingabe bestätigen.
    /// Wird bestätigt, lädt die Methode das Update herunter, entpackt es (ohne den "Output"-Ordner) und 
    /// erstellt eine temporäre Batch-Datei, die nach Beendigung der Anwendung die neue EXE über die alte kopiert 
    /// und die aktualisierte Version startet.
    /// </summary>
    static async Task CheckForSoftwareUpdateAsync()
    {
        // Zunächst prüfen, ob ein Update verfügbar ist
        string username = "schulzep16";
        string repoName = "LS25-GitHub-Mod-Downloader";

        try
        {
            var release = await GitHubService.GetLatestReleaseAsync(username, repoName);
            if (release == null)
            {
                Console.WriteLine("Kein Update gefunden.");
                return;
            }
            string latestVersion = release.TagName.TrimStart('v');

            var versions = await GetCurrentVersionsAsync();
            string currentVersion = versions.ContainsKey("ProgramVersion") ? versions["ProgramVersion"] : null;

            if (currentVersion != latestVersion)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Es ist ein Update verfügbar!");
                Console.WriteLine($"Aktuelle Version: {currentVersion ?? "Nicht installiert"}");
                Console.WriteLine($"Neue Version: {latestVersion}");
                Console.WriteLine("Drücke Enter, um das Update durchzuführen oder eine beliebige andere Taste, um es zu überspringen...");
                Console.ResetColor();

                var key = Console.ReadKey(true);
                // Nur wenn Enter gedrückt wird, wird das Update durchgeführt
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine("Update wird durchgeführt...");

                    // Falls Admin-Rechte nicht vorhanden sind, Neustart mit Admin-Rechten
                    if (!IsUserAdministrator())
                    {
                        Console.WriteLine("Das Update benötigt Administratorrechte. Starte Programm neu mit Admin-Rechten...");
                        RestartAsAdmin();
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
        catch (HttpRequestException e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Netzwerkfehler beim Überprüfen der Software-Version: {e.Message}");
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Fehler beim Überprüfen der Software-Version: {e.Message}");
        }
        finally
        {
            Console.ResetColor();
        }
    }

    static void RestartAsAdmin()
    {
        var exePath = Process.GetCurrentProcess().MainModule.FileName;
        var startInfo = new ProcessStartInfo(exePath)
        {
            Verb = "runas", // Startet mit Admin-Rechten
            UseShellExecute = true
        };
        try
        {
            Process.Start(startInfo);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Fehler beim Neustart als Admin: {e.Message}");
        }
        Environment.Exit(0);
    }

    static async Task PerformUpdateAsync(GitHubRelease release, string latestVersion, Dictionary<string, string> versions)
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

            using (var updateResponse = await client.GetAsync(downloadUrl))
            {
                updateResponse.EnsureSuccessStatusCode();
                using (var fs = new FileStream(tempZipPath, FileMode.Create))
                {
                    await updateResponse.Content.CopyToAsync(fs);
                }
            }

            // Falls ein Signature-Asset vorhanden ist, lade es herunter und führe die Signaturprüfung durch.
            var sigAsset = release.Assets.FirstOrDefault(a => a.BrowserDownloadUrl.EndsWith(".sig", StringComparison.OrdinalIgnoreCase));
            if (sigAsset != null)
            {
                string sigFilePath = Path.Combine(Path.GetTempPath(), $"{repoName}_update.sig");
                using (var sigResponse = await client.GetAsync(sigAsset.BrowserDownloadUrl))
                {
                    sigResponse.EnsureSuccessStatusCode();
                    using (var fs = new FileStream(sigFilePath, FileMode.Create))
                    {
                        await sigResponse.Content.CopyToAsync(fs);
                    }
                }
                if (!VerifyFileSignature(tempZipPath, sigFilePath))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Die Signaturprüfung des Updates ist fehlgeschlagen.");
                    Console.ResetColor();
                    // Aufräumen
                    if (File.Exists(sigFilePath))
                        File.Delete(sigFilePath);
                    if (File.Exists(tempZipPath))
                        File.Delete(tempZipPath);
                    return;
                }
                // Signaturprüfung erfolgreich; Signaturdatei löschen.
                File.Delete(sigFilePath);
            }
            else
            {
                Console.WriteLine("Warnung: Keine Signatur gefunden. Update wird ohne Signaturprüfung durchgeführt.");
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

                    // Bestimme den kompletten Zielpfad
                    string destinationPath = Path.Combine(tempExtractPath, entry.FullName);

                    // Falls es sich um einen Ordner handelt, diesen erstellen
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
                Verb = "runas", // Startet die Batch-Datei mit Administratorrechten
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
                // Bereinige temporäre Dateien
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
    /// Überprüft die Signatur der heruntergeladenen Datei. 
    /// (Hier als Platzhalter – in einer produktiven Umgebung sollte hier die tatsächliche Signaturprüfung erfolgen.)
    /// </summary>
    static bool VerifyFileSignature(string filePath, string signatureFilePath)
    {
        Console.WriteLine("Signaturprüfung wird durchgeführt...");
        // TODO: Implementiere hier die echte Signaturprüfung.
        return true;
    }

    /// <summary>
    /// Löscht temporäre Dateien bzw. Verzeichnisse.
    /// </summary>
    static void CleanupTempFiles(List<string> paths)
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

    /// <summary>
    /// Startet den Farming Simulator 2025.
    /// </summary>
    static void StartFarmingSimulator()
    {
        string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string configPath = Path.Combine(exeDirectory, "farming_simulator_path.txt");
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string[] shortcutFiles = Directory.GetFiles(desktopPath, "*.lnk");

        string farmingSimulatorShortcut = shortcutFiles.FirstOrDefault(f => Path.GetFileName(f).Contains("Farming Simulator"));

        if (farmingSimulatorShortcut != null)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = farmingSimulatorShortcut,
                UseShellExecute = true
            });
            Console.WriteLine("Farming Simulator wird gestartet...");
            Environment.Exit(0);
        }
        else if (File.Exists(configPath))
        {
            string savedPath = File.ReadAllText(configPath).Trim('"');
            if (File.Exists(savedPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = savedPath,
                    UseShellExecute = true
                });
                Console.WriteLine("Farming Simulator wird aus der gespeicherten Datei gestartet...");
                Environment.Exit(0);
            }
        }
        else
        {
            Console.WriteLine("Keine passende Verknüpfung auf dem Desktop gefunden. Bitte geben Sie den Pfad zur Farming Simulator EXE an:");
            string userPath = Console.ReadLine().Trim('"');
            if (File.Exists(userPath))
            {
                File.WriteAllText(configPath, userPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = userPath,
                    UseShellExecute = true
                });
                Console.WriteLine("Farming Simulator wird gestartet und der Pfad wurde gespeichert...");
                Environment.Exit(0);
            }
            else
            {
                Console.WriteLine("Der eingegebene Pfad ist ungültig.");
            }
        }
    }

    /// <summary>
    /// Vergleicht zwei Dateien, indem zunächst die Dateigrößen und anschließend die Datei-Inhalte in Blöcken verglichen werden.
    /// </summary>
    private static bool CompareFiles(string file1, string file2)
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
            int bytesRead1;
            int bytesRead2;
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

    static List<GitHubProject> ScanModFolderForKnownMods()
    {
        var foundProjects = new List<GitHubProject>();
        // Definiere bekannte Mods (Mapping von Dateiname zu GitHubProject)
        var knownMods = new Dictionary<string, GitHubProject>
        {
            { "FS25_UniversalAutoload", new GitHubProject { Username = "loki79uk", Repo = "FS25_UniversalAutoload" } },
            { "Courseplay_FS25", new GitHubProject { Username = "Courseplay", Repo = "Courseplay_FS25" } },
            { "FS25_AutoDrive", new GitHubProject { Username = "Stephan-S", Repo = "FS25_AutoDrive" } }
        };

        if (Directory.Exists(ModFolder))
        {
            var zipFiles = Directory.GetFiles(ModFolder, "*.zip");
            foreach (var file in zipFiles)
            {
                var modName = Path.GetFileNameWithoutExtension(file);
                if (knownMods.TryGetValue(modName, out var project))
                {
                    foundProjects.Add(project);
                }
            }
        }
        return foundProjects;
    }

    /// <summary>
    /// Prüft, ob das Programm mit Administratorrechten ausgeführt wird.
    /// </summary>
    /// <returns></returns>
    private static bool IsUserAdministrator()
    {
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
        {
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
