using System;
using System.Collections.Generic;
using System.IO;

namespace LS25ModDownloader
{
    public static class FileHelper
    {
        public static bool CompareFiles(string file1, string file2)
        {
            FileInfo fi1 = new FileInfo(file1);
            FileInfo fi2 = new FileInfo(file2);
            if (fi1.Length != fi2.Length)
                return false;
            const int bufferSize = 8192;
            using (FileStream fs1 = File.OpenRead(file1))
            using (FileStream fs2 = File.OpenRead(file2))
            {
                byte[] buffer1 = new byte[bufferSize];
                byte[] buffer2 = new byte[bufferSize];
                int bytesRead1;
                int bytesRead2;
                while ((bytesRead1 = fs1.Read(buffer1, 0, bufferSize)) > 0)
                {
                    bytesRead2 = fs2.Read(buffer2, 0, bufferSize);
                    if (bytesRead1 != bytesRead2)
                        return false;
                    for (int i = 0; i < bytesRead1; i++)
                    {
                        if (buffer1[i] != buffer2[i])
                            return false;
                    }
                }
            }
            return true;
        }

        public static void CleanupTempFiles(List<string> paths)
        {
            foreach (var path in paths)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    else if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Fehler beim Löschen temporärer Datei/Ordner {path}: {ex.Message}");
                }
            }
        }
    }
}
