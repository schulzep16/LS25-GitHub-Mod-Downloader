using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace LS25ModDownloader
{
    public class ModManager
    {
        private readonly Config _config;
        private readonly HttpClient _client;
        private readonly VersionManager _versionManager;
        public List<GitHubProject> Projects { get; private set; } = new List<GitHubProject>();

        public ModManager(Config config, HttpClient client, VersionManager versionManager)
        {
            _config = config;
            _client = client;
            _versionManager = versionManager;
        }

        public async Task LoadProjectsAsync()
        {
            if (File.Exists(_config.ProjectsFile))
            {
                try
                {
                    await using var stream = File.OpenRead(_config.ProjectsFile);
                    Projects = await JsonSerializer.DeserializeAsync<List<GitHubProject>>(stream) ?? new List<GitHubProject>();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Fehler beim Laden der Projekte.");
                }
            }
            else
            {
                // Scan im Mod-Ordner nach bekannten Mods
                Projects = ScanModFolderForKnownMods();
                if (!Projects.Any())
                {
                    if (UserInteraction.AskYesNo("Es wurden keine Mods eingebunden. Möchten Sie die standardmäßig eingebundenen Mods hinzufügen? (J/N): "))
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

        public async Task SaveProjectsAsync()
        {
            try
            {
                await using var stream = File.Create(_config.ProjectsFile);
                await JsonSerializer.SerializeAsync(stream, Projects, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fehler beim Speichern der Projekte.");
            }
        }

        public void DisplayIntegratedMods()
        {
            Console.WriteLine("Aktuell berücksichtigte Mods:");
            if (!Projects.Any())
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

        public async Task ManageProjectsAsync()
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
                    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(repo))
                    {
                        Console.WriteLine("Username und Repository-Name dürfen nicht leer sein.");
                        break;
                    }
                    bool valid = await GitHubService.ValidateProjectAsync(_client, username, repo);
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
                    if (!Projects.Any())
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
                        var removedProject = Projects[index - 1];
                        Projects.RemoveAt(index - 1);
                        await SaveProjectsAsync();
                        Console.WriteLine("Projekt gelöscht.");
                        // Entferne den Eintrag in der Version-Datei
                        var versions = await _versionManager.GetCurrentVersionsAsync();
                        if (versions.Remove(removedProject.Repo))
                        {
                            await _versionManager.SaveCurrentVersionsAsync(versions);
                            Console.WriteLine("Zugehöriger Eintrag in der Version-Datei entfernt.");
                        }
                        if (UserInteraction.AskYesNo("Soll auch der Mod aus dem Mod-Ordner gelöscht werden? (J/N): "))
                        {
                            var deletedFiles = Directory.EnumerateFiles(_config.ModFolder, "*.zip")
                                .Where(f => Path.GetFileNameWithoutExtension(f).Equals(removedProject.Repo, StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            if (deletedFiles.Count > 0)
                            {
                                foreach (var file in deletedFiles)
                                {
                                    File.Delete(file);
                                    Log.Information("Mod-Datei gelöscht: {File}", file);
                                }
                                Console.WriteLine("Mod-Datei(en) wurden gelöscht.");
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

        public async Task InitializeCurrentVersionsAsync()
        {
            var versions = await _versionManager.GetCurrentVersionsAsync();
            bool updated = false;
            // Entferne Einträge, die nicht mehr benötigt werden
            var keysToRemove = versions.Keys.Where(k => k != "ProgramVersion" && !Projects.Any(p => p.Repo == k)).ToList();
            foreach (var key in keysToRemove)
            {
                versions.Remove(key);
                updated = true;
            }
            // Füge fehlende Einträge hinzu
            foreach (var project in Projects)
            {
                if (!versions.ContainsKey(project.Repo))
                {
                    versions[project.Repo] = new Version(0, 0, 0);
                    updated = true;
                }
            }
            if (updated)
            {
                await _versionManager.SaveCurrentVersionsAsync(versions);
            }
        }

        public async Task CheckAndUpdateModAsync(GitHubProject project)
        {
            string username = project.Username;
            string repoName = project.Repo;
            Console.WriteLine($"Prüfe Updates für {repoName}...");
            var versions = await _versionManager.GetCurrentVersionsAsync();
            try
            {
                var release = await GitHubService.GetLatestReleaseAsync(_client, username, repoName);
                if (release == null)
                {
                    Console.WriteLine($"Kein Release gefunden für {repoName}.");
                    return;
                }
                if (!Version.TryParse(release.TagName?.TrimStart('v'), out Version? latestVersion) || latestVersion is null)
                {
                    Log.Warning("Ungültige Version aus GitHub für {RepoName}: {TagName}", repoName, release.TagName);
                    return;
                }
                Version currentVersion = versions.ContainsKey(repoName) ? versions[repoName] : new Version(0, 0, 0);

                // Dateiname direkt von GitHub übernehmen (z.B. FS25_Courseplay.zip)
                string? downloadUrl = release.Assets?.FirstOrDefault()?.BrowserDownloadUrl;
                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    Console.WriteLine("Download-URL nicht gefunden.");
                    return;
                }
                string assetFileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
                string modZipPath = Path.Combine(_config.ModFolder, assetFileName);

                // Wenn die Datei nicht existiert, Version zurücksetzen
                if (!File.Exists(modZipPath))
                {
                    currentVersion = new Version(0, 0, 0);
                    versions[repoName] = currentVersion;
                }

                if (currentVersion < latestVersion)
                {
                    Log.Warning("Neue Version gefunden für {RepoName}: {LatestVersion} (Aktuelle Version: {CurrentVersion})", repoName, latestVersion, currentVersion);

                    // Alle vorhandenen Zips dieses Mods löschen (verhindet Duplikate)
                    foreach (var oldFile in Directory.EnumerateFiles(_config.ModFolder, "*.zip")
                        .Where(f => !f.Equals(modZipPath, StringComparison.OrdinalIgnoreCase)
                                 && Path.GetFileNameWithoutExtension(f).Equals(repoName, StringComparison.OrdinalIgnoreCase)))
                    {
                        Log.Information("Lösche alte Mod-Datei: {OldFile}", oldFile);
                        File.Delete(oldFile);
                    }

                    var downloadResponse = await _client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    downloadResponse.EnsureSuccessStatusCode();
                    await using (var stream = await downloadResponse.Content.ReadAsStreamAsync())
                    await using (var fs = new FileStream(modZipPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 8192, useAsync: true))
                    {
                        await stream.CopyToAsync(fs);
                    }
                    string extractedVersionStr = await ExtractVersionFromZipAsync(modZipPath);
                    if (!Version.TryParse(extractedVersionStr, out Version? extractedVersion) || extractedVersion is null)
                    {
                        extractedVersion = new Version(0, 0, 0);
                      }
                    if (extractedVersion == latestVersion)
                    {
                        Log.Information("{RepoName} erfolgreich aktualisiert auf Version {LatestVersion}", repoName, latestVersion);
                        versions[repoName] = latestVersion;
                        // Versionsdaten in die current_version.txt speichern:
                        await _versionManager.SaveCurrentVersionsAsync(versions);
                    }
                    else
                    {
                        Log.Error("Fehler: Heruntergeladene Version von {RepoName} stimmt nicht mit der erwarteten Version überein.", repoName);
                        File.Delete(modZipPath);
                    }
                }
                else
                {
                    Log.Information("{RepoName} ist bereits auf dem neuesten Stand.", repoName);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fehler beim Überprüfen von {RepoName}", repoName);
            }
        }

        /// <summary>
        /// Liest die Versionsnummer aus dem Zip-Archiv aus – zuerst aus "modesc.xml", falls vorhanden, sonst aus "modDesc.xml".
        /// </summary>
        private Task<string> ExtractVersionFromZipAsync(string zipPath)
        {
            return Task.Run(() =>
            {
                using var zip = ZipFile.OpenRead(zipPath);
                var entry = zip.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith("modDesc.xml", StringComparison.OrdinalIgnoreCase));
                if (entry != null)
                {
                    using var stream = entry.Open();
                    var doc = XDocument.Load(stream);
                    return doc.Root?.Element("version")?.Value ?? "unknown";
                }
                return "unknown";
            });
        }


        private List<GitHubProject> ScanModFolderForKnownMods()
        {
            var foundProjects = new List<GitHubProject>();
            var knownMods = new Dictionary<string, GitHubProject>
            {
                { "FS25_UniversalAutoload", new GitHubProject { Username = "loki79uk", Repo = "FS25_UniversalAutoload" } },
                { "Courseplay_FS25", new GitHubProject { Username = "Courseplay", Repo = "Courseplay_FS25" } },
                { "FS25_AutoDrive", new GitHubProject { Username = "Stephan-S", Repo = "FS25_AutoDrive" } }
            };

            if (Directory.Exists(_config.ModFolder))
            {
                var zipFiles = Directory.GetFiles(_config.ModFolder, "*.zip");
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
    }
}
