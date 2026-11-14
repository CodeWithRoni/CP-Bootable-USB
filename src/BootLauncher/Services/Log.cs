using System;
using System.IO;
using System.Text;
using CP.Shared;

namespace BootLauncher.Services
{
    /// <summary>
    /// Simple file logger. Writes to the USB (CP_WIN_DATA\CP\logs) when available so the shop can
    /// read what happened on a customer PC later; falls back to %TEMP% (X:\ RAM disk in WinPE).
    /// </summary>
    public static class Log
    {
        private static readonly object Sync = new object();
        private static string _path = Path.Combine(Path.GetTempPath(), $"bootlauncher-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        public static event Action<string>? LineWritten;

        public static string FilePath => _path;

        /// <summary>Moves logging to the USB data partition once it has been found.</summary>
        public static void UseDataRoot(string? dataRoot)
        {
            if (string.IsNullOrEmpty(dataRoot)) return;
            try
            {
                var dir = Path.Combine(dataRoot, Constants.LogsFolder);
                Directory.CreateDirectory(dir);
                var newPath = Path.Combine(dir, $"bootlauncher-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                lock (Sync)
                {
                    if (File.Exists(_path)) File.Copy(_path, newPath, true);
                    _path = newPath;
                }
            }
            catch { /* USB may be read-only; keep temp log */ }
        }

        public static void Info(string msg) => Write("INFO ", msg);
        public static void Warn(string msg) => Write("WARN ", msg);
        public static void Error(string msg) => Write("ERROR", msg);
        public static void Detail(string msg) => Write("     ", msg);

        private static void Write(string level, string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {level} {msg}";
            lock (Sync)
            {
                try { File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8); } catch { }
            }
            LineWritten?.Invoke(line);
        }
    }
}
