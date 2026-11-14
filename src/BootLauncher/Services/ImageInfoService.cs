using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BootLauncher.Models;

namespace BootLauncher.Services
{
    /// <summary>
    /// Reads the editions inside a mounted Windows ISO with DISM.
    /// Supports normal media (\sources\install.wim|esd) and dual-architecture media (\x86\sources, \x64\sources).
    /// </summary>
    public static class ImageInfoService
    {
        private static readonly string[] SourceFolders = { "sources", @"x64\sources", @"x86\sources" };
        private static readonly string[] ImageNames = { "install.wim", "install.esd" };

        public static async Task<List<ImageEdition>> ReadEditionsAsync(string isoRoot, CancellationToken ct)
        {
            var editions = new List<ImageEdition>();
            var foundSwm = false;

            foreach (var folder in SourceFolders)
            {
                var dir = Path.Combine(isoRoot, folder);
                if (!Directory.Exists(dir)) continue;

                var image = ImageNames.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists);
                if (image == null)
                {
                    if (File.Exists(Path.Combine(dir, "install.swm"))) foundSwm = true;
                    continue;
                }

                var setupExe = File.Exists(Path.Combine(dir, "setup.exe")) ? Path.Combine(dir, "setup.exe") : Path.Combine(isoRoot, "setup.exe");
                editions.AddRange(await ReadImageAsync(image, setupExe, ct));
            }

            if (editions.Count == 0)
            {
                if (foundSwm)
                    throw new InvalidOperationException("This ISO uses a split image (install.swm), which is not supported. Use an ISO with install.wim or install.esd.");
                throw new InvalidOperationException("No install.wim / install.esd found — this does not look like a Windows installation ISO.");
            }
            return editions;
        }

        private static async Task<List<ImageEdition>> ReadImageAsync(string imageFile, string setupExe, CancellationToken ct)
        {
            var dism = ProcessRunner.System32("dism.exe");
            Log.Info("Reading editions from " + imageFile);

            var list = await ProcessRunner.RunAsync(dism, $"/English /Get-WimInfo /WimFile:\"{imageFile}\"", null, ct);
            if (list.ExitCode != 0)
                throw new InvalidOperationException("DISM could not read " + Path.GetFileName(imageFile) + ". The ISO may be damaged.");

            var result = new List<ImageEdition>();
            ImageEdition? current = null;
            foreach (var raw in list.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                var m = Regex.Match(line, @"^Index\s*:\s*(\d+)");
                if (m.Success)
                {
                    current = new ImageEdition { ImageFile = imageFile, SetupExe = setupExe, Index = int.Parse(m.Groups[1].Value) };
                    result.Add(current);
                    continue;
                }
                m = Regex.Match(line, @"^Name\s*:\s*(.+)$");
                if (m.Success && current != null) current.Name = m.Groups[1].Value.Trim();
            }

            // Architecture + version are per index; the first index is representative for the whole file.
            if (result.Count > 0)
            {
                var detail = await ProcessRunner.RunAsync(dism, $"/English /Get-WimInfo /WimFile:\"{imageFile}\" /Index:{result[0].Index}", null, ct);
                var arch = Match(detail.Output, @"Architecture\s*:\s*(\S+)");
                var version = Match(detail.Output, @"Version\s*:\s*([\d\.]+)");
                var build = Match(detail.Output, @"ServicePack Build\s*:\s*(\d+)");
                var family = FamilyText.FromVersion(version);

                foreach (var e in result)
                {
                    e.Arch = arch;
                    e.Version = string.IsNullOrEmpty(build) ? version : $"{version}.{build}";
                    e.Family = family;
                    if (string.IsNullOrEmpty(e.Name)) e.Name = $"{FamilyText.Name(family)} (index {e.Index})";
                }
            }

            Log.Info($"{result.Count} edition(s) found in {Path.GetFileName(imageFile)}.");
            return result;
        }

        private static string Match(string text, string pattern)
        {
            var m = Regex.Match(text, pattern);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }
    }
}
