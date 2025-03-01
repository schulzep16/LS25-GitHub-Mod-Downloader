using System;
using System.IO;
using System.Threading.Tasks;
using Serilog;

namespace LS25ModDownloader
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Konfiguration des Loggings mit Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try
            {
                Log.Information("Programm gestartet.");

                // Konfiguration laden (config.json wird automatisch angelegt, falls nicht vorhanden)
                var config = await ConfigManager.LoadConfigAsync("config.json");

                // Sicherstellen, dass der Mod-Ordner existiert
                Directory.CreateDirectory(config.ModFolder);

                // Gemeinsame HttpClient-Instanz
                var httpClient = HttpClientProvider.Instance;

                // Initialisiere Services
                var versionManager = new VersionManager(config);
                var modManager = new ModManager(config, httpClient, versionManager);
                var updateService = new UpdateService(config, httpClient, versionManager);

                // Programm-Update prüfen
                await updateService.CheckForSoftwareUpdateAsync();

                // Projekte/Mods verwalten
                await modManager.LoadProjectsAsync();
                modManager.DisplayIntegratedMods();
                await modManager.ManageProjectsAsync();

                // Mod-Versionen prüfen und ggf. aktualisieren 
                await modManager.InitializeCurrentVersionsAsync();
                foreach (var project in modManager.Projects)
                {
                    await modManager.CheckAndUpdateModAsync(project);
                }

                // Hole die aktuellen Versionen und speichere sie
                var versions = await versionManager.GetCurrentVersionsAsync();
                await versionManager.SaveCurrentVersionsAsync(versions);

                // Startet den Farming Simulator, falls gewünscht
                if (UserInteraction.AskYesNo("Möchten Sie den Farming Simulator 2025 starten? (J/N): "))
                {
                    GameStarter.StartFarmingSimulator(config);
                }

                Log.Information("Programm beendet.");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Ein schwerwiegender Fehler ist aufgetreten.");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}