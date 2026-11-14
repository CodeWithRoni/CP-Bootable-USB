using System.IO;
using System.Reflection;
using System.Text;
using CP.Shared;
using CP.Shared.Models;
using UsbMaker.Models;

namespace UsbMaker.Services;

public sealed class DeploymentRequest
{
    public required UsbDiskInfo Disk { get; init; }
    public required CpConfig Config { get; init; }
    public string? LogoSourcePath { get; init; }
    public required PayloadInfo Payload { get; init; }
    public DataFileSystem DataFileSystem { get; init; } = DataFileSystem.NTFS;
    public int BootPartitionSizeMb { get; init; } = Constants.DefaultBootPartitionSizeMb;
    public bool VerifyAfterCopy { get; init; } = true;
}

public sealed record DeploymentResult(char BootLetter, char DataLetter, ulong DataFreeBytes);

/// <summary>Orchestrates the full "Create Bootable USB" and "Update Branding Only" workflows.</summary>
public sealed class DeploymentService
{
    private readonly LogService _log;
    private readonly UsbDetectionService _detection;
    private readonly DiskPartService _diskPart;
    private readonly BootSetupService _boot;
    private readonly PayloadService _payload;

    public DeploymentService(LogService log, UsbDetectionService detection)
    {
        _log = log;
        _detection = detection;
        _diskPart = new DiskPartService(log);
        _boot = new BootSetupService(log);
        _payload = new PayloadService(log);
    }

    public static string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    // ------------------------------------------------------------------ Create

    public async Task<DeploymentResult> CreateAsync(DeploymentRequest req, IProgress<DeploymentProgress> progress, CancellationToken ct)
    {
        void Report(double percent, string status) => progress.Report(new DeploymentProgress(percent, status));

        _log.Info($"=== Create Bootable USB — CP Bootable USB {AppVersion} ===");
        _log.Info($"Target: {req.Disk.DisplayName}");

        // 1. Payload sanity
        Report(1, "Checking boot payload...");
        if (!req.Payload.IsValid) throw new InvalidOperationException(req.Payload.Summary);
        var bootBytesAvailable = (long)req.BootPartitionSizeMb * 1024 * 1024;
        if (req.Payload.TotalBytes > bootBytesAvailable - 32L * 1024 * 1024)
            throw new InvalidOperationException(
                $"The boot payload ({UsbDiskInfo.FormatBytes((ulong)req.Payload.TotalBytes)}) does not fit in a " +
                $"{req.BootPartitionSizeMb} MB boot partition. Increase the boot partition size.");
        _log.Info(req.Payload.Summary);

        // 2. Re-verify the disk is still the same physical device (protects against re-plugging).
        Report(3, "Re-checking selected disk...");
        var current = await Task.Run(() => _detection.FindByNumber(req.Disk.Number), ct)
                      ?? throw new InvalidOperationException($"Disk {req.Disk.Number} is no longer connected.");
        if (current.UniqueId != req.Disk.UniqueId || current.SizeBytes != req.Disk.SizeBytes)
            throw new InvalidOperationException("The USB drive changed since it was selected. Refresh and select it again.");
        if (current.IsProtected) throw new InvalidOperationException(current.ProtectedReason);
        if (current.SizeBytes < (ulong)(req.BootPartitionSizeMb + 1024) * 1024 * 1024)
            throw new InvalidOperationException("The USB drive is too small.");

        ct.ThrowIfCancellationRequested();

        // 3. Partition + format — point of no return.
        var letters = DriveLetterHelper.GetFreeLetters(2);
        char bootLetter = letters[0], dataLetter = letters[1];
        _log.Info($"Using drive letters: BOOT = {bootLetter}:, DATA = {dataLetter}:");
        Report(5, "Partitioning and formatting (DiskPart)...");
        await _diskPart.PartitionAsync(req.Disk.Number, req.BootPartitionSizeMb, bootLetter, dataLetter, req.DataFileSystem, ct);

        Report(15, "Waiting for new volumes...");
        await DriveLetterHelper.WaitForVolumeAsync(bootLetter, TimeSpan.FromSeconds(60), ct);
        await DriveLetterHelper.WaitForVolumeAsync(dataLetter, TimeSpan.FromSeconds(60), ct);
        _log.Success("Both partitions are mounted.");

        var bootRoot = $"{bootLetter}:\\";
        var dataRoot = $"{dataLetter}:\\";

        // 4. BIOS boot code
        Report(18, "Writing BIOS boot code...");
        await _boot.WriteBootCodeAsync(req.Payload.BootsectPath!, bootLetter, ct);

        // 5. Copy WinPE payload (biggest step: 20% → 80%)
        Report(20, "Copying WinPE boot files...");
        var copyProgress = new Progress<double>(f => Report(20 + f * 60, $"Copying WinPE boot files... {f:P0}"));
        await _payload.CopyMediaAsync(req.Payload, bootRoot, new SyncProgress<double>(copyProgress), ct);

        // 6. BCD stores
        Report(81, "Configuring BIOS and UEFI boot menus...");
        var desc = $"{req.Config.Branding?.SoftwareName ?? BrandingDefaults.SoftwareName} - {req.Config.Branding?.BrandName ?? BrandingDefaults.BrandName}";
        await _boot.CustomizeBcdAsync(bootLetter, desc, addX86Entry: req.Payload.HasX86, ct);
        if (req.Payload.HasX86)
            _log.Info("32-bit WinPE included: on Legacy BIOS PCs a menu offers 64-bit (default) or 32-bit for 10 seconds.");
        await WriteMarkerAsync(Path.Combine(bootRoot, Constants.BootMarkerFile), "CP Bootable USB boot partition");

        // 7. DATA partition structure + branding
        Report(85, "Writing branding and folder structure...");
        await PrepareDataPartitionAsync(dataRoot, ct);
        await WriteBrandingAsync(dataRoot, req.Config, req.LogoSourcePath, ct);

        // 8. Verify
        if (req.VerifyAfterCopy)
        {
            Report(90, "Verifying...");
            await _payload.VerifyAsync(req.Payload, bootRoot, ct);
        }
        var check = ConfigService.Load(Path.Combine(dataRoot, Constants.ConfigFileName));
        if (!check.LoadedFromFile) throw new IOException("config.json could not be read back from the USB.");

        CopySessionLog(dataRoot);

        var free = (ulong)new DriveInfo(dataLetter.ToString()).AvailableFreeSpace;
        Report(100, "Done! Your CP Bootable USB is ready.");
        _log.Success($"CP Bootable USB is ready. Copy Windows ISO files to {dataRoot}{Constants.IsoFolder}\\ ({UsbDiskInfo.FormatBytes(free)} free).");
        return new DeploymentResult(bootLetter, dataLetter, free);
    }

    // ------------------------------------------------------------------ Update branding only

    public async Task UpdateBrandingAsync(UsbDiskInfo disk, CpConfig config, string? logoSourcePath,
        IProgress<DeploymentProgress> progress, CancellationToken ct)
    {
        _log.Info($"=== Update Branding Only — {disk.DisplayName} ===");
        progress.Report(new DeploymentProgress(10, "Locating CP_WIN_DATA partition..."));

        var dataLetter = disk.CpDataLetter ??
                         disk.Partitions.FirstOrDefault(p => string.Equals(p.VolumeLabel, Constants.DataVolumeLabel, StringComparison.OrdinalIgnoreCase))?.DriveLetter
                         ?? throw new InvalidOperationException(
                             "This USB drive does not look like a CP Bootable USB (CP_WIN_DATA partition not found). Use 'Create Bootable USB' instead.");

        var dataRoot = $"{dataLetter}:\\";
        progress.Report(new DeploymentProgress(40, "Writing config.json and logo..."));
        await PrepareDataPartitionAsync(dataRoot, ct);   // re-creates any missing folders, keeps ISOs
        await WriteBrandingAsync(dataRoot, config, logoSourcePath, ct);

        if (disk.CpBootLetter is char bootLetter)
        {
            progress.Report(new DeploymentProgress(75, "Updating boot menu name..."));
            var desc = $"{config.Branding?.SoftwareName} - {config.Branding?.BrandName}";
            // addX86Entry: false — the 32-bit entry already exists from creation; never duplicate it.
            await _boot.CustomizeBcdAsync(bootLetter, desc, addX86Entry: false, ct);
        }
        else
        {
            _log.Info("BOOT partition has no drive letter; boot menu name left unchanged (it is hidden at boot anyway).");
        }

        CopySessionLog(dataRoot);
        progress.Report(new DeploymentProgress(100, "Branding updated."));
        _log.Success("Branding updated. ISO files were not touched.");
    }

    // ------------------------------------------------------------------ helpers

    private async Task PrepareDataPartitionAsync(string dataRoot, CancellationToken ct)
    {
        foreach (var folder in new[]
                 {
                     Constants.IsoFolder, Constants.AppFolder, Constants.LogsFolder,
                     Constants.Win7DriversX86, Constants.Win7DriversX64
                 })
        {
            Directory.CreateDirectory(Path.Combine(dataRoot, folder));
        }

        var isoReadme = Path.Combine(dataRoot, Constants.IsoFolder, "README.txt");
        if (!File.Exists(isoReadme))
            await File.WriteAllTextAsync(isoReadme,
                "Copy your Windows ISO files (Windows 7 / 8 / 8.1 / 10 / 11, 32-bit or 64-bit) into this folder.\r\n" +
                "They will appear automatically in the CP Bootable USB boot screen.\r\n", Encoding.UTF8, ct);

        var driversReadme = Path.Combine(dataRoot, Constants.DriversFolder, "README.txt");
        if (!File.Exists(driversReadme))
            await File.WriteAllTextAsync(driversReadme,
                "Windows 7 drivers (.inf) for USB 3.0, NVMe and chipset go into Win7\\x86 or Win7\\x64.\r\n" +
                "They are injected automatically when Windows 7 is installed with the Direct Apply engine.\r\n", Encoding.UTF8, ct);

        await WriteMarkerAsync(Path.Combine(dataRoot, Constants.DataMarkerFile), "CP Bootable USB data partition");
    }

    private async Task WriteBrandingAsync(string dataRoot, CpConfig config, string? logoSourcePath, CancellationToken ct)
    {
        var normalized = ConfigService.Normalize(config, null);
        var appFolder = Path.Combine(dataRoot, Constants.AppFolder);

        // Remove old logos so only the current one exists.
        foreach (var old in Directory.EnumerateFiles(appFolder, "logo.*"))
        {
            try { File.Delete(old); } catch { /* ignore */ }
        }

        normalized.Branding!.LogoPath = null;
        if (!string.IsNullOrWhiteSpace(logoSourcePath) && File.Exists(logoSourcePath))
        {
            var ext = Path.GetExtension(logoSourcePath).ToLowerInvariant();
            var relative = Constants.LogoFileBaseName + ext;
            await using (var src = File.OpenRead(logoSourcePath))
            await using (var dst = File.Create(Path.Combine(dataRoot, relative)))
                await src.CopyToAsync(dst, ct);
            normalized.Branding.LogoPath = relative;
            _log.Info($"Logo copied to {relative}.");
        }
        else
        {
            _log.Info("No custom logo selected — the built-in logo will be shown.");
        }

        normalized.Meta = new MetaSection
        {
            CreatedBy = $"UsbMaker {AppVersion}",
            CreatedAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz")
        };

        ConfigService.Save(normalized, Path.Combine(dataRoot, Constants.ConfigFileName));
        _log.Success($"{Constants.ConfigFileName} written.");
    }

    private static Task WriteMarkerAsync(string path, string text) =>
        File.WriteAllTextAsync(path, $"{text}\r\ncreated={DateTimeOffset.Now:O}\r\nby=UsbMaker {AppVersion}\r\n", Encoding.UTF8);

    private void CopySessionLog(string dataRoot)
    {
        try
        {
            var dst = Path.Combine(dataRoot, Constants.LogsFolder, Path.GetFileName(_log.LogFilePath));
            File.Copy(_log.LogFilePath, dst, overwrite: true);
        }
        catch { /* non-critical */ }
    }
}

/// <summary>Wraps an IProgress so it can be called from a worker thread without blocking.</summary>
internal sealed class SyncProgress<T> : IProgress<T>
{
    private readonly IProgress<T> _inner;
    private DateTime _last = DateTime.MinValue;

    public SyncProgress(IProgress<T> inner) => _inner = inner;

    public void Report(T value)
    {
        // Throttle to ~10 updates per second so the UI thread is not flooded during the 500 MB copy.
        var now = DateTime.UtcNow;
        if ((now - _last).TotalMilliseconds < 100) return;
        _last = now;
        _inner.Report(value);
    }
}
