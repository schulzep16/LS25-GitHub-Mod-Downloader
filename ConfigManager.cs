using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace LS25ModDownloader
{
    public class Config
    {
        public required string ModFolder { get; set; }
        public required string CurrentVersionFile { get; set; }
        public required string ProjectsFile { get; set; }
        public required string GitHubUser { get; set; }
        public required string GitHubRepo { get; set; }
        // Weitere Konfigurationseinstellungen können hier ergänzt werden
    }

    public static class ConfigManager
    {
        public static async Task<Config> LoadConfigAsync(string path)
        {
            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize<Config>(json)
                    ?? throw new InvalidOperationException($"Konfigurationsdatei '{path}' konnte nicht gelesen werden.");
            }
            else
            {
                // Standardkonfiguration
                var config = new Config
                {
                    ModFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "FarmingSimulator2025", "mods"),
                    CurrentVersionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "current_version.txt"),
                    ProjectsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "projects.json"),
                    GitHubUser = "schulzep16",
                    GitHubRepo = "LS25-GitHub-Mod-Downloader"
                };
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(path, json);
                return config;
            }
        }
    }
}