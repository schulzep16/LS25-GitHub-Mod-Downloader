using System;
using System.Security.Principal;
using System.Diagnostics;

namespace LS25ModDownloader
{
    public static class UserInteraction
    {
        public static bool AskYesNo(string message)
        {
            Console.Write(message);
            var input = Console.ReadLine();
            return !string.IsNullOrEmpty(input) && input.Trim().ToUpper().StartsWith("J");
        }

        public static bool IsUserAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public static void RestartAsAdmin()
        {
            var exePath = Process.GetCurrentProcess().MainModule.FileName;
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
