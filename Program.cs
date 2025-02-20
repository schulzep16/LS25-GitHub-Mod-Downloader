using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

public class GitHubProject
{
    public string Username { get; set; }
    public string Repo { get; set; }
}

class Program
{
    // Aktuelle Version der Software
    private const string DefaultVersion = "1.0.0";

    private static readonly string ModFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "My Games", "FarmingSimulator2025", "mods");
    private static readonly string CurrentVersionFile = Path.Combine(ModFolder, "current_version.txt");
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
            // Standardprojekte, falls die Datei noch nicht existiert
            Projects = new List<GitHubProject>
            {
                new GitHubProject { Username = "loki79uk", Repo = "FS25_UniversalAutoload" },
                new GitHubProject { Username = "Courseplay", Repo = "Courseplay_FS25" },
                new GitHubProject { Username = "Stephan-S", Repo = "FS25_AutoDrive" }
            };
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

                // Sicherstellen, dass der User-Agent gesetzt ist
                if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("request");

                // Prüfe, ob das Projekt existiert (über den Latest Release-Endpunkt)
                string checkUrl = $"https://api.github.com/repos/{username}/{repo}/releases/latest";
                try
                {
                    var response = await client.GetAsync(checkUrl);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Projekt nicht gefunden oder es existieren keine Releases.");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Fehler beim Prüfen des Projekts: " + ex.Message);
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
                    string? modDeleteChoice = Console.ReadLine();
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
        Console.WriteLine(); // Leerzeile für bessere Übersicht
    }

    static async Task CheckAndUpdateModAsync(GitHubProject project, Dictionary<string, string> currentVersions)
    {
        string username = project.Username, repoName = project.Repo;
        Console.WriteLine($"Prüfe Updates für {repoName}...");

        try
        {
            var latestReleaseUrl = $"https://api.github.com/repos/{username}/{repoName}/releases/latest";
            var response = await client.GetStringAsync(latestReleaseUrl);

            dynamic jsonResponse = JsonConvert.DeserializeObject(response);
            string latestVersion = jsonResponse?.tag_name?.ToString()?.TrimStart('v') ?? "unknown";
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

                string downloadUrl = jsonResponse.assets[0].browser_download_url?.ToString();
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
        // GitHub-Repository-Informationen (anpassen, falls nötig)
        string username = "schulzep16";
        string repoName = "LS25-GitHub-Mod-Downloader";

        try
        {
            var latestReleaseUrl = $"https://api.github.com/repos/{username}/{repoName}/releases/latest";
            var request = new HttpRequestMessage(HttpMethod.Get, latestReleaseUrl);
            request.Headers.UserAgent.ParseAdd("request");

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            dynamic jsonResponse = JsonConvert.DeserializeObject(await response.Content.ReadAsStringAsync());
            string latestVersion = jsonResponse?.tag_name?.ToString()?.TrimStart('v') ?? "unknown";

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

                    // Download-URL des neuen Releases (hier wird der erste Asset angenommen)
                    string downloadUrl = jsonResponse.assets[0].browser_download_url?.ToString();
                    if (string.IsNullOrWhiteSpace(downloadUrl))
                    {
                        Console.WriteLine("Download-URL nicht gefunden.");
                        return;
                    }

                    // Download des Update-Zips in einen temporären Ordner
                    string tempZipPath = Path.Combine(Path.GetTempPath(), $"{repoName}_update.zip");
                    using (var updateResponse = await client.GetAsync(downloadUrl))
                    {
                        updateResponse.EnsureSuccessStatusCode();
                        using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write))
                        {
                            await updateResponse.Content.CopyToAsync(fs);
                        }
                    }

                    // Entpacke das Update in einen temporären Ordner, überspringe dabei den "Output"-Ordner
                    string tempExtractPath = Path.Combine(Path.GetTempPath(), $"{repoName}_update");
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
                                // Stelle sicher, dass das Verzeichnis existiert, und extrahiere dann die Datei
                                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                                entry.ExtractToFile(destinationPath, overwrite: true);
                            }
                        }
                    }

                    // Verwende nun den tempExtractPath als Quelle für die Aktualisierung
                    string sourceFolder = tempExtractPath;
                    string currentExecutablePath = Process.GetCurrentProcess().MainModule.FileName;
                    string currentDirectory = Path.GetDirectoryName(currentExecutablePath);
                    string batchFilePath = Path.Combine(Path.GetTempPath(), "update.bat");
                    string batchContent = $@"@echo off
echo Warte auf Beendigung der Anwendung...
ping 127.0.0.1 -n 5 >nul
echo Update wird installiert...
xcopy /Y /E ""{sourceFolder}\*"" ""{currentDirectory}\""
echo Update installiert.
start """" ""{currentExecutablePath}""
del ""%~f0""";

                    File.WriteAllText(batchFilePath, batchContent);

                    // Starte die Batch-Datei und beende die Anwendung
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = batchFilePath,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true
                    });
                    Console.WriteLine("Update wird durchgeführt. Die Anwendung wird jetzt beendet.");

                    // Überprüfe, ob alle Dateien erfolgreich aktualisiert wurden
                    bool updateSuccessful = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories)
                        .All(tempFile =>
                        {
                            string relativePath = Path.GetRelativePath(sourceFolder, tempFile);
                            string targetFile = Path.Combine(currentDirectory, relativePath);
                            return File.Exists(targetFile) && CompareFiles(tempFile, targetFile);
                        });

                    if (updateSuccessful)
                    {
                        // Aktualisiere die Versionsnummer in der current_version.txt
                        versions["ProgramVersion"] = latestVersion;
                        await SaveCurrentVersionsAsync(versions);
                    }
                    else
                    {
                        Console.WriteLine("Fehler: Nicht alle Dateien wurden erfolgreich aktualisiert.");
                    }

                    Environment.Exit(0);
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
}
