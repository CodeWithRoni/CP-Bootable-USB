using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using BootLauncher.Models;
using CP.Shared;
using CP.Shared.Models;

namespace BootLauncher.Services
{
    public sealed class InstallProgress
    {
        public InstallProgress(double percent, string step) { Percent = percent; Step = step; }
        public double Percent { get; }
        public string Step { get; }
    }

    /// <summary>Engine A: optional LabConfig bypass, then the ISO's own Windows Setup.</summary>
    public static class SetupEngine
    {
        public static void ApplyBypass(BypassSection b)
        {
            if (!(b.Tpm || b.SecureBoot || b.Ram || b.Cpu || b.Storage)) return;
            using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\Setup\LabConfig"))
            {
                if (key == null) throw new InvalidOperationException("Could not open HKLM\\SYSTEM\\Setup\\LabConfig.");
                if (b.Tpm) key.SetValue("BypassTPMCheck", 1, RegistryValueKind.DWord);
                if (b.SecureBoot) key.SetValue("BypassSecureBootCheck", 1, RegistryValueKind.DWord);
                if (b.Ram) key.SetValue("BypassRAMCheck", 1, RegistryValueKind.DWord);
                if (b.Cpu) key.SetValue("BypassCPUCheck", 1, RegistryValueKind.DWord);
                if (b.Storage) key.SetValue("BypassStorageCheck", 1, RegistryValueKind.DWord);
            }
            Log.Info($"LabConfig bypass written (TPM={b.Tpm}, SecureBoot={b.SecureBoot}, RAM={b.Ram}, CPU={b.Cpu}, Storage={b.Storage}).");
        }

        public static void Launch(ImageEdition e)
        {
            if (!File.Exists(e.SetupExe)) throw new FileNotFoundException("setup.exe not found on the ISO.", e.SetupExe);
            Log.Info("Launching " + e.SetupExe);
            Process.Start(new ProcessStartInfo(e.SetupExe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(e.SetupExe) ?? ""
            });
        }
    }

    /// <summary>
    /// Engine B: Direct Apply — partition the target disk, DISM-apply the image, add Win7 drivers,
    /// write boot files with bcdboot. Works for 32-bit and Windows 7 images from a 64-bit WinPE.
    /// </summary>
    public sealed class DirectApplyEngine
    {
        private readonly IProgress<InstallProgress> _progress;
        private readonly CancellationToken _ct;

        public DirectApplyEngine(IProgress<InstallProgress> progress, CancellationToken ct)
        {
            _progress = progress;
            _ct = ct;
        }

        public async Task RunAsync(TargetDisk disk, ImageEdition e, bool uefi)
        {
            Log.Info($"=== Direct Apply: {e.Name} ({e.Arch}, {e.Version}) → Disk {disk.Index} [{(uefi ? "UEFI/GPT" : "BIOS/MBR")}] ===");
            if (!uefi && disk.SizeBytes > 2UL * 1024 * 1024 * 1024 * 1024)
                Log.Warn("Disk is larger than 2 TB — in Legacy/MBR mode only the first 2 TB can be used.");

            var letters = FreeLetters(2);
            char sys = letters[0], win = letters[1];

            // 1. Partition
            Report(2, "Partitioning disk " + disk.Index + "...");
            var script = uefi
                ? $"select disk {disk.Index}\r\nclean\r\nconvert gpt\r\n" +
                  $"create partition efi size=260\r\nformat quick fs=fat32 label=\"System\"\r\nassign letter={sys}\r\n" +
                  "create partition msr size=16\r\n" +
                  $"create partition primary\r\nformat quick fs=ntfs label=\"Windows\"\r\nassign letter={win}\r\nexit\r\n"
                : $"select disk {disk.Index}\r\nclean\r\nconvert mbr\r\n" +
                  $"create partition primary size=500\r\nformat quick fs=ntfs label=\"System Reserved\"\r\nactive\r\nassign letter={sys}\r\n" +
                  $"create partition primary\r\nformat quick fs=ntfs label=\"Windows\"\r\nassign letter={win}\r\nexit\r\n";

            var scriptPath = Path.Combine(Path.GetTempPath(), "cp_target.txt");
            File.WriteAllText(scriptPath, script, Encoding.ASCII);
            var dp = await ProcessRunner.RunAsync(ProcessRunner.System32("diskpart.exe"), $"/s \"{scriptPath}\"", Log.Detail, _ct, killOnCancel: false);
            if (dp.ExitCode != 0) throw new InvalidOperationException("DiskPart could not prepare the target disk. See the log for details.");
            await WaitForDrive(win);
            await WaitForDrive(sys);

            // 2. Apply image (5% → 90%)
            Report(5, "Applying Windows image (this takes 5–20 minutes)...");
            var dism = ProcessRunner.System32("dism.exe");
            var apply = await ProcessRunner.RunAsync(dism,
                $"/English /Apply-Image /ImageFile:\"{e.ImageFile}\" /Index:{e.Index} /ApplyDir:{win}:\\",
                line =>
                {
                    var m = Regex.Match(line, @"(\d{1,3}(?:\.\d)?)%");
                    if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                        Report(5 + pct * 0.85, $"Applying Windows image... {pct:0}%");
                    else
                        Log.Detail(line);
                }, _ct, killOnCancel: false);
            if (apply.ExitCode != 0) throw new InvalidOperationException($"DISM failed to apply the image (exit {apply.ExitCode}). See the log for details.");

            // 3. Windows 7 drivers
            if (e.Family == WindowsFamily.Win7) await InjectWin7DriversAsync(win, e.Arch);

            // 4. Boot files
            Report(95, "Writing boot files (bcdboot)...");
            var bcd = await ProcessRunner.RunAsync(ProcessRunner.System32("bcdboot.exe"),
                $"{win}:\\Windows /s {sys}: /f {(uefi ? "UEFI" : "BIOS")}", Log.Detail, _ct, killOnCancel: false);
            if (bcd.ExitCode != 0) throw new InvalidOperationException($"bcdboot failed (exit {bcd.ExitCode}). The PC may not start Windows.");

            Report(100, "Windows was installed successfully.");
            Log.Info("Direct Apply finished.");
        }

        private async Task InjectWin7DriversAsync(char win, string arch)
        {
            var cfg = BrandingService.Config.Install;
            if (cfg?.InjectDriversForWin7 == false || BrandingService.DataRoot == null) return;

            var folder = Path.Combine(BrandingService.DataRoot, cfg?.DriversFolder ?? Constants.DriversFolder, "Win7",
                arch.Equals("x86", StringComparison.OrdinalIgnoreCase) ? "x86" : "x64");
            if (!Directory.Exists(folder) || !Directory.EnumerateFiles(folder, "*.inf", SearchOption.AllDirectories).Any())
            {
                Log.Warn("No Windows 7 drivers found in " + folder + " — USB 3.0 / NVMe may not work after install.");
                return;
            }

            Report(91, "Adding Windows 7 drivers (USB 3.0 / NVMe)...");
            var r = await ProcessRunner.RunAsync(ProcessRunner.System32("dism.exe"),
                $"/English /Image:{win}:\\ /Add-Driver /Driver:\"{folder}\" /Recurse", Log.Detail, _ct, killOnCancel: false);
            if (r.ExitCode != 0) Log.Warn($"Some drivers could not be added (DISM exit {r.ExitCode}).");
            else Log.Info("Windows 7 drivers added.");
        }

        private void Report(double pct, string step) => _progress.Report(new InstallProgress(pct, step));

        private static async Task WaitForDrive(char letter)
        {
            for (var i = 0; i < 40; i++)
            {
                try { if (new DriveInfo(letter.ToString()).IsReady) return; } catch { }
                await Task.Delay(500);
            }
            throw new InvalidOperationException($"Drive {letter}: did not appear after partitioning.");
        }

        private static List<char> FreeLetters(int count)
        {
            var used = new HashSet<char>(Environment.GetLogicalDrives().Select(d => char.ToUpperInvariant(d[0])));
            var preferred = new[] { 'S', 'W', 'T', 'V', 'R', 'Q', 'P', 'O', 'N', 'M' };
            var free = preferred.Where(c => !used.Contains(c)).Take(count).ToList();
            if (free.Count < count) throw new InvalidOperationException("Not enough free drive letters.");
            return free;
        }
    }

    public static class PowerService
    {
        public static void Reboot() => Run("reboot", "/r /t 0");
        public static void Shutdown() => Run("shutdown", "/s /t 0");

        private static void Run(string wpeVerb, string shutdownArgs)
        {
            var wpeutil = ProcessRunner.System32("wpeutil.exe");
            if (SystemInfo.IsWinPE && File.Exists(wpeutil))
                Process.Start(new ProcessStartInfo(wpeutil, wpeVerb) { UseShellExecute = false, CreateNoWindow = true });
            else
                Process.Start(new ProcessStartInfo(ProcessRunner.System32("shutdown.exe"), shutdownArgs) { UseShellExecute = false, CreateNoWindow = true });
        }

        public static void OpenCommandPrompt()
        {
            Process.Start(new ProcessStartInfo(ProcessRunner.System32("cmd.exe"))
            {
                UseShellExecute = true,
                WorkingDirectory = BrandingService.DataRoot ?? Environment.SystemDirectory
            });
        }
    }
}
