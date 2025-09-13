using System;
using System.IO;
using System.Text;

namespace Buds3ProAideAuditivelA.v2.Services
{
    public static class LogService
    {
        private static readonly object _lock = new();
        private static readonly string _logFile =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "app.log");

        public static void Append(string message)
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFile)!);
                File.AppendAllText(_logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }

        public static string ReadAll() =>
            File.Exists(_logFile) ? File.ReadAllText(_logFile, Encoding.UTF8) : string.Empty;

        public static void Clear()
        {
            if (File.Exists(_logFile)) File.Delete(_logFile);
        }

        public static string PathFile => _logFile;
    }
}
