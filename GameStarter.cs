using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace LS25ModDownloader
{
    public static class GameStarter
    {
        public static void StartFarmingSimulator(Config config)
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
    }
}
