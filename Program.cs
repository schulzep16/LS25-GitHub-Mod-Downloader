using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json;

public class GitHubProject
{
    public string Username { get; set; }
    public string Repo { get; set; }
}

class Program
{
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

        // Projekte aus der JSON-Datei laden (oder Default-Werte setzen)
        await LoadProjectsAsync();

        // Dem Nutzer die Möglichkeit geben, Projekte zu verwalten
        await ManageProjectsAsync();

        Console.WriteLine("Starte Überprüfung der Mods...");
        await InitializeCurrentVersionsAsync();
        var currentVersions = await GetCurrentVersionsAsync();

        client.DefaultRequestHeaders.UserAgent.ParseAdd("request");

        foreach (var project in Projects)
        {
            await CheckAndUpdateModAsync(project, currentVersions);
        }

        await SaveCurrentVersionsAsync(currentVersions);
        Console.WriteLine("Überprüfung abgeschlossen.");
        Console.WriteLine("Drücke Enter, um das Programm zu beenden.");
        Console.ReadLine();
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
                    Projects.RemoveAt(index - 1);
                    await SaveProjectsAsync();
                    Console.WriteLine("Projekt gelöscht.");
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

                var downloadUrl = jsonResponse.assets[0].browser_download_url?.ToString();
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
            foreach (var line in await File.ReadAllLinesAsync(CurrentVersionFile))
            {
                var parts = line.Split(':');
                if (parts.Length == 2)
                {
                    versions[parts[0]] = parts[1];
                }
            }
        }
        return versions;
    }

    static async Task SaveCurrentVersionsAsync(Dictionary<string, string> versions)
    {
        using (var sw = new StreamWriter(CurrentVersionFile))
        {
            foreach (var kvp in versions)
            {
                await sw.WriteLineAsync($"{kvp.Key}:{kvp.Value}");
            }
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

        foreach (var project in Projects)
        {
            string repoName = project.Repo;
            string modZipPath = Path.Combine(ModFolder, $"{repoName}.zip");

            if (!versions.ContainsKey(repoName) || versions[repoName] == "Not Installed" || !File.Exists(modZipPath))
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
}
