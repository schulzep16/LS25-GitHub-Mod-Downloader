using Serilog;

namespace LS25ModDownloader
{
    public static class SecurityHelper
    {
        /// <summary>
        /// Platzhalter für eine spätere RSA/SHA256-Signaturprüfung.
        /// Solange nicht implementiert, wird die Prüfung übersprungen.
        /// </summary>
        public static bool VerifyFileSignature(string filePath, string signatureFilePath)
        {
            Log.Warning("Signaturprüfung nicht implementiert – Datei {FilePath} wird ohne Prüfung akzeptiert.", filePath);
            return true;
        }
    }
}
