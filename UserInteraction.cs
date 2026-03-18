using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace LS25ModDownloader
{
    public static class UserInteraction
    {
        public static bool AskYesNo(string message)
        {
            Console.Write(message);
            var input = Console.ReadLine();
            return !string.IsNullOrEmpty(input) && input.Trim().StartsWith("J", StringComparison.OrdinalIgnoreCase);
        }

        [SupportedOSPlatform("windows")]
        public static bool IsUserAdministrator()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        [SupportedOSPlatform("windows")]
        public static void RestartAsAdmin()
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Konnte Pfad zur ausführbaren Datei nicht ermitteln.");
            var startInfo = new ProcessStartInfo(exePath)
            {
                Verb = "runas",
                UseShellExecute = true
            };
            try
            {
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler beim Neustart als Admin: {ex.Message}");
            }
            Environment.Exit(0);
        }
    }
}
