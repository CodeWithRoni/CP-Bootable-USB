using System.IO;
using System.Text;
using CP.Shared;

namespace UsbMaker.Services;

public enum DataFileSystem { NTFS, exFAT }

/// <summary>
/// Creates the CP layout with DiskPart:
///   P1 CP_WIN_BOOT  FAT32, active, MBR type 0x0C  (boots BIOS + UEFI)
///   P2 CP_WIN_DATA  NTFS/exFAT, rest of the disk  (ISOs, config.json)
/// </summary>
public sealed class DiskPartService
{
    private readonly LogService _log;

    public DiskPartService(LogService log) => _log = log;

    public static string BuildScript(uint diskNumber, int bootSizeMb, char bootLetter, char dataLetter, DataFileSystem fs)
    {
        var fsName = fs == DataFileSystem.NTFS ? "ntfs" : "exfat";
        var sb = new StringBuilder();
        sb.AppendLine($"select disk {diskNumber}");
        sb.AppendLine("attributes disk clear readonly noerr");
        sb.AppendLine("clean");
        sb.AppendLine("convert mbr");
        // Partition 1: BOOT (first on disk so old BIOSes can always reach it).
        sb.AppendLine($"create partition primary size={bootSizeMb}");
        sb.AppendLine("set id=0c override");                 // FAT32 LBA; 0xEF would hide it from Windows
        sb.AppendLine($"format fs=fat32 quick label={Constants.BootVolumeLabel}");
        sb.AppendLine("active");                              // Legacy BIOS boots the active partition
        sb.AppendLine($"assign letter={bootLetter}");
        // Partition 2: DATA (rest of the disk).
        sb.AppendLine("create partition primary");
        sb.AppendLine($"format fs={fsName} quick label={Constants.DataVolumeLabel}");
        sb.AppendLine($"assign letter={dataLetter}");
        sb.AppendLine("exit");
        return sb.ToString();
    }

    /// <summary>Runs the partition script. Retries once, because Explorer or AV sometimes briefly locks a fresh USB.</summary>
    public async Task PartitionAsync(uint diskNumber, int bootSizeMb, char bootLetter, char dataLetter,
        DataFileSystem fs, CancellationToken ct)
    {
        var script = BuildScript(diskNumber, bootSizeMb, bootLetter, dataLetter, fs);
        var scriptPath = Path.Combine(Path.GetTempPath(), $"cp_diskpart_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(scriptPath, script, Encoding.ASCII, ct);

        _log.Info($"DiskPart script for disk {diskNumber}:");
        foreach (var line in script.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            _log.Detail("  " + line.TrimEnd());

        try
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                var diskpart = Path.Combine(Environment.SystemDirectory, "diskpart.exe");
                // Never kill diskpart half-way: that can leave the disk with no partition table.
                var result = await ProcessRunner.RunAsync(diskpart, $"/s \"{scriptPath}\"", _log.Detail, ct, killOnCancel: false);

                if (result.ExitCode == 0)
                {
                    _log.Success("DiskPart finished successfully.");
                    return;
                }

                _log.Warn($"DiskPart failed with exit code {result.ExitCode} (attempt {attempt}/2).");
                if (attempt == 1)
                {
                    _log.Info("Close any Explorer windows showing the USB drive. Retrying in 5 seconds...");
                    await Task.Delay(5000, ct);
                }
            }

            throw new InvalidOperationException(
                "DiskPart could not partition the USB drive. Close all programs using it, re-plug it, and try again. " +
                "See the log for DiskPart's output.");
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* ignore */ }
        }
    }
}
