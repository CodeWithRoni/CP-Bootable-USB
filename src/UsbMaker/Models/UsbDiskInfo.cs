namespace UsbMaker.Models;

public sealed class PartitionInfo
{
    public uint PartitionNumber { get; init; }
    public char? DriveLetter { get; init; }
    public ulong SizeBytes { get; init; }
    public string? VolumeLabel { get; init; }
    public string? FileSystem { get; init; }
}

/// <summary>A USB-attached disk as reported by the Windows Storage WMI provider (MSFT_Disk).</summary>
public sealed class UsbDiskInfo
{
    public uint Number { get; init; }
    public string FriendlyName { get; init; } = "";
    public ulong SizeBytes { get; init; }
    public string PartitionStyle { get; init; } = "RAW";
    public string UniqueId { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public bool IsReadOnly { get; init; }
    public bool IsOffline { get; init; }
    public IReadOnlyList<PartitionInfo> Partitions { get; init; } = Array.Empty<PartitionInfo>();

    /// <summary>Set when the disk must never be erased (e.g. UsbMaker itself runs from it).</summary>
    public string? ProtectedReason { get; init; }
    public bool IsProtected => ProtectedReason != null;

    /// <summary>Drive letter of an existing CP_WIN_DATA partition (marker file found), if any.</summary>
    public char? CpDataLetter { get; init; }
    /// <summary>Drive letter of an existing CP_WIN_BOOT partition, if any.</summary>
    public char? CpBootLetter { get; init; }
    public bool IsCpUsb => CpDataLetter != null;

    public const ulong LargeDiskThreshold = 256UL * 1024 * 1024 * 1024;
    public bool IsLarge => SizeBytes > LargeDiskThreshold;

    public string SizeText => FormatBytes(SizeBytes);

    public string LettersText
    {
        get
        {
            var letters = Partitions.Where(p => p.DriveLetter != null).Select(p => p.DriveLetter + ":").ToList();
            return letters.Count == 0 ? "no drive letter" : string.Join(" ", letters);
        }
    }

    public string DisplayName => $"Disk {Number}: {FriendlyName} — {SizeText} [{LettersText}]";

    public string DetailsText
    {
        get
        {
            var lines = new List<string>
            {
                $"Disk number: {Number}",
                $"Model: {FriendlyName}",
                $"Capacity: {SizeText} ({SizeBytes:N0} bytes)",
                $"Partition style: {PartitionStyle}",
                $"Serial: {(string.IsNullOrWhiteSpace(SerialNumber) ? "n/a" : SerialNumber.Trim())}",
            };
            foreach (var p in Partitions)
            {
                var letter = p.DriveLetter != null ? $"{p.DriveLetter}:" : "--";
                lines.Add($"  Partition {p.PartitionNumber}: {letter} {p.VolumeLabel} {p.FileSystem} {FormatBytes(p.SizeBytes)}".TrimEnd());
            }
            lines.Add(IsCpUsb ? "Status: existing CP Bootable USB detected" : "Status: not a CP Bootable USB");
            return string.Join(Environment.NewLine, lines);
        }
    }

    public string WarningText
    {
        get
        {
            var w = new List<string>();
            if (IsProtected) w.Add($"PROTECTED: {ProtectedReason}");
            if (IsReadOnly) w.Add("Disk is marked read-only (a hardware write-protect switch cannot be cleared by software).");
            if (IsOffline) w.Add("Disk is offline.");
            if (IsLarge) w.Add("This is a large disk (over 256 GB) — make sure it is not an external backup drive!");
            if (SizeBytes < 8UL * 1024 * 1024 * 1024) w.Add("Disk is smaller than 8 GB — not enough for most Windows ISOs.");
            return string.Join(Environment.NewLine, w);
        }
    }

    public static string FormatBytes(ulong bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {units[i]}";
    }

    public override string ToString() => DisplayName;
}
