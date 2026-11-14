using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BootLauncher.Services
{
    /// <summary>
    /// Mounts an ISO and returns its drive root.
    /// 1st try: PowerShell Mount-DiskImage (WinPE-PowerShell + WinPE-StorageWMI).
    /// 2nd try: diskpart "attach vdisk readonly" (Virtual Disk API, works without PowerShell).
    /// </summary>
    public sealed class IsoMountService
    {
        public string? MountedIso { get; private set; }
        public string? MountedRoot { get; private set; }
        private bool _viaDiskpart;

        public async Task<string> MountAsync(string isoPath, CancellationToken ct)
        {
            if (MountedIso != null && string.Equals(MountedIso, isoPath, StringComparison.OrdinalIgnoreCase) && MountedRoot != null && Directory.Exists(MountedRoot))
                return MountedRoot;

            await DismountAsync();
            Log.Info("Mounting " + isoPath);

            var before = Letters();
            string? root = null;

            // --- PowerShell
            try
            {
                var p = isoPath.Replace("'", "''");
                var r = await ProcessRunner.PowerShellAsync(
                    $"$i = Get-DiskImage -ImagePath '{p}'; if (-not $i.Attached) {{ $i = Mount-DiskImage -ImagePath '{p}' -StorageType ISO -PassThru }}; " +
                    "Start-Sleep -Milliseconds 800; ($i | Get-Volume).DriveLetter", ct);
                var letter = r.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim()).LastOrDefault(l => l.Length == 1 && char.IsLetter(l[0]));
                if (r.ExitCode == 0 && letter != null) root = letter + @":\";
            }
            catch (Exception ex) { Log.Warn("Mount-DiskImage failed: " + ex.Message); }

            // --- diskpart fallback
            if (root == null)
            {
                Log.Info("Trying diskpart attach vdisk...");
                var script = Path.Combine(Path.GetTempPath(), "cp_mount.txt");
                File.WriteAllText(script, $"select vdisk file=\"{isoPath}\"\r\nattach vdisk readonly\r\nexit\r\n", Encoding.ASCII);
                await ProcessRunner.RunAsync(ProcessRunner.System32("diskpart.exe"), $"/s \"{script}\"", Log.Detail, ct, killOnCancel: false);
                _viaDiskpart = true;
                root = await WaitForNewSourcesDriveAsync(before, ct);
            }

            if (root == null || !Directory.Exists(root))
                throw new InvalidOperationException("The ISO could not be mounted. The file may be damaged or not a Windows installation ISO.");

            MountedIso = isoPath;
            MountedRoot = root;
            Log.Info("ISO mounted at " + root);
            return root;
        }

        public async Task DismountAsync()
        {
            if (MountedIso == null) return;
            var iso = MountedIso;
            MountedIso = null;
            MountedRoot = null;
            try
            {
                if (_viaDiskpart)
                {
                    var script = Path.Combine(Path.GetTempPath(), "cp_unmount.txt");
                    File.WriteAllText(script, $"select vdisk file=\"{iso}\"\r\ndetach vdisk\r\nexit\r\n", Encoding.ASCII);
                    await ProcessRunner.RunAsync(ProcessRunner.System32("diskpart.exe"), $"/s \"{script}\"", Log.Detail, CancellationToken.None, false);
                }
                else
                {
                    await ProcessRunner.PowerShellAsync($"Dismount-DiskImage -ImagePath '{iso.Replace("'", "''")}' | Out-Null");
                }
                Log.Info("ISO dismounted.");
            }
            catch (Exception ex) { Log.Warn("Dismount: " + ex.Message); }
            _viaDiskpart = false;
        }

        private static string[] Letters()
        {
            try { return DriveInfo.GetDrives().Select(d => d.Name).ToArray(); } catch { return new string[0]; }
        }

        private static async Task<string?> WaitForNewSourcesDriveAsync(string[] before, CancellationToken ct)
        {
            for (var i = 0; i < 30; i++)
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (before.Contains(d.Name, StringComparer.OrdinalIgnoreCase)) continue;
                    try { if (d.IsReady && Directory.Exists(Path.Combine(d.Name, "sources"))) return d.Name; } catch { }
                }
                await Task.Delay(500, ct);
            }
            return null;
        }
    }
}
