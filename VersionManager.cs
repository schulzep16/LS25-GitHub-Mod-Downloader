using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace LS25ModDownloader
{
    public class VersionManager
    {
        private readonly Config _config;
        public VersionManager(Config config)
        {
            _config = config;
        }

        public async Task<Dictionary<string, Version>> GetCurrentVersionsAsync()
        {
            var versions = new Dictionary<string, Version>();
            if (File.Exists(_config.CurrentVersionFile))
            {
                try
                {
                    var lines = await File.ReadAllLinesAsync(_config.CurrentVersionFile);
                    foreach (var line in lines)
                    {
                        var parts = line.Split(':');
                        if (parts.Length == 2 && Version.TryParse(parts[1], out Version ver))
                        {
                            versions[parts[0]] = ver;
                        }
                        else
                        {
                            Log.Warning("Ungültige Versionszeile: {Line}", line);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Fehler beim Lesen der Versionen.");
                }
            }
            return versions;
        }

        public async Task SaveCurrentVersionsAsync(Dictionary<string, Version> versions)
        {
            try
            {
                using (var sw = new StreamWriter(_config.CurrentVersionFile))
                {
                    foreach (var kvp in versions)
                    {
                        await sw.WriteLineAsync($"{kvp.Key}:{kvp.Value}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fehler beim Speichern der Versionen.");
            }
        }
    }
}
