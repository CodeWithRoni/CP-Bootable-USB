using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BootLauncher.Models;
using CP.Shared;

namespace BootLauncher.Services
{
    /// <summary>Finds *.iso files in the configured folders (ISO\ and drive root) on all drives.</summary>
    public static class IsoScanner
    {
        public static List<IsoItem> Scan()
        {
            var cfg = BrandingService.Config.Launcher;
            var folders = cfg?.IsoFolders ?? new List<string> { Constants.IsoFolder, "" };
            var scanAll = cfg?.ScanAllDrives ?? true;
            var system = Path.GetPathRoot(Environment.SystemDirectory);

            var roots = new List<string>();
            if (scanAll)
            {
                foreach (var d in SafeDrives())
                {
                    try
                    {
                        if (!d.IsReady) continue;
                        if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                        if (string.Equals(d.Name, system, StringComparison.OrdinalIgnoreCase)) continue; // X:\ in WinPE
                        roots.Add(d.RootDirectory.FullName);
                    }
                    catch { }
                }
            }
            else if (BrandingService.DataRoot != null)
            {
                roots.Add(BrandingService.DataRoot);
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<IsoItem>();
            foreach (var root in roots)
            foreach (var folder in folders)
            {
                var dir = string.IsNullOrEmpty(folder) ? root : Path.Combine(root, folder);
                string[] files;
                try { files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.iso", SearchOption.TopDirectoryOnly) : new string[0]; }
                catch { continue; }

                foreach (var f in files)
                {
                    if (!seen.Add(f)) continue;
                    try
                    {
                        var item = new IsoItem { Path = f, SizeBytes = (ulong)new FileInfo(f).Length, VolumeLabel = ReadVolumeLabel(f) };
                        Guess(item);
                        result.Add(item);
                    }
                    catch (Exception ex) { Log.Warn($"Skipping {f}: {ex.Message}"); }
                }
            }

            Log.Info($"ISO scan: {result.Count} file(s) in {roots.Count} drive(s).");
            return result.OrderBy(i => i.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static DriveInfo[] SafeDrives()
        {
            try { return DriveInfo.GetDrives(); } catch { return new DriveInfo[0]; }
        }

        /// <summary>Reads the ISO9660 volume label (primary volume descriptor, sector 16) without mounting.</summary>
        public static string ReadVolumeLabel(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var buf = new byte[2048];
                    fs.Seek(16 * 2048, SeekOrigin.Begin);
                    if (fs.Read(buf, 0, buf.Length) != buf.Length) return "";
                    if (Encoding.ASCII.GetString(buf, 1, 5) != "CD001") return "";
                    return Encoding.ASCII.GetString(buf, 40, 32).Trim();
                }
            }
            catch { return ""; }
        }

        /// <summary>Quick guess from file name + volume label. The real values come from DISM after mounting.</summary>
        private static void Guess(IsoItem item)
        {
            var s = (item.FileName + " " + item.VolumeLabel).ToUpperInvariant().Replace('_', ' ').Replace('-', ' ');

            if (Has(s, "WIN11", "WINDOWS 11", "W11", "WIN 11")) item.GuessFamily = WindowsFamily.Win11;
            else if (Has(s, "WIN10", "WINDOWS 10", "W10", "WIN 10")) item.GuessFamily = WindowsFamily.Win10;
            else if (Has(s, "8.1", "WIN81", "IR5 ", "IR3 ")) item.GuessFamily = WindowsFamily.Win81;
            else if (Has(s, "WIN8", "WINDOWS 8", "HRM ")) item.GuessFamily = WindowsFamily.Win8;
            else if (Has(s, "WIN7", "WINDOWS 7", "W7", "GRMC", "GSP1RMC")) item.GuessFamily = WindowsFamily.Win7;
            else if (Has(s, "CCCOMA", "CCSA", "CPBA", "CENA")) item.GuessFamily = WindowsFamily.Win10; // 10 or 11 — confirmed after analysis

            if (Has(s, "X64", "AMD64", "64BIT", "64 BIT", "ULXFRER")) item.GuessArch = "x64";
            else if (Has(s, "X86", "32BIT", "32 BIT", "ULFRER")) item.GuessArch = "x86";
            if (Has(s, "X86 X64", "X64 X86", "AIO", "DUAL")) item.GuessArch = "x86 + x64";
        }

        private static bool Has(string s, params string[] keys) => keys.Any(k => s.IndexOf(k, StringComparison.Ordinal) >= 0);
    }
}
