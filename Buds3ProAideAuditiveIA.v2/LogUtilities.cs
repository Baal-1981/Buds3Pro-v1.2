using System;
using System.IO;
using Android.Content;

namespace Buds3ProAideAuditiveIA.v2
{
    /// <summary>
    /// Utilitaire de logs avec horodatage. Écrit à la fois dans AppLog (affichage UI)
    /// et dans un fichier texte persistant (~/Android/data/.../files/logs/sonara-YYYYMMDD.txt).
    /// </summary>
    public static class LogUtilities
    {
        private static readonly object _lock = new object();

        private static string GetLogDir(Context ctx)
        {
            try
            {
                var dir = ctx.GetExternalFilesDir("logs");
                if (dir != null && !Directory.Exists(dir.AbsolutePath))
                    Directory.CreateDirectory(dir.AbsolutePath);
                return dir?.AbsolutePath ?? ctx.FilesDir.AbsolutePath;
            }
            catch
            {
                return ctx?.FilesDir?.AbsolutePath ?? "/sdcard";
            }
        }

        public static void Log(Context ctx, string tag, string message)
        {
            var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = $"[{ts}] {tag}: {message}";

            try { AppLog.Append(line); } catch { /* UI log best-effort */ }

            try
            {
                if (ctx == null) return;
                var dir = GetLogDir(ctx);
                var file = Path.Combine(dir, $"sonara-{DateTime.Now:yyyyMMdd}.log");
                lock (_lock) File.AppendAllText(file, line + Environment.NewLine);
            }
            catch { /* disk log best-effort */ }
        }

        public static void LogRoute(Context ctx, string route)
        {
            Log(ctx, "ROUTE", route ?? "(null)");
        }

        public static void LogLatency(Context ctx, int transportMs, int algoMs)
        {
            Log(ctx, "LATENCY", $"transport={transportMs}ms algo={algoMs}ms total={transportMs + algoMs}ms");
        }

        public static void LogDeviceSummary(Context ctx, string summary)
        {
            Log(ctx, "DEVICE", summary ?? "(null)");
        }
    }
}