using System;
using System.IO;

namespace Atlas8PowerButton
{
    internal static class Log
    {
        private const long MaxBytes = 512 * 1024;
        private static readonly object Gate = new object();

        public static string FilePath => Path.Combine(Settings.DataDirectory, "Atlas8PowerButton.log");

        public static void Write(string message)
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Settings.DataDirectory);
                    var file = new FileInfo(FilePath);
                    if (file.Exists && file.Length > MaxBytes)
                    {
                        var old = FilePath + ".old";
                        File.Delete(old);
                        File.Move(FilePath, old);
                    }

                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
                }
                catch
                {
                    // Logging must never take the button handling down.
                }
            }
        }
    }
}
