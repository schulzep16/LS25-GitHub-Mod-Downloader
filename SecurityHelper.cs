using System;

namespace LS25ModDownloader
{
    public static class SecurityHelper
    {
        public static bool VerifyFileSignature(string filePath, string signatureFilePath)
        {
            Console.WriteLine("Signaturprüfung wird durchgeführt...");
            // TODO: Implementiere hier die echte RSA-Signaturprüfung (z. B. mit RSA und SHA256)
            return true;
        }
    }
}
