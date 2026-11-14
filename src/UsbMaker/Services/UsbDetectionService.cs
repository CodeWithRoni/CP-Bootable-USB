using System.IO;
using System.Management;
using CP.Shared;
using UsbMaker.Models;

namespace UsbMaker.Services;

/// <summary>
/// Enumerates USB disks through the Windows Storage WMI provider (root\Microsoft\Windows\Storage).
/// System and boot disks are never returned, whatever bus they are on.
/// </summary>
public sealed class UsbDetectionService
{
    private const string StorageNamespace = @"\\.\root\Microsoft\Windows\Storage";
    private const ushort BusTypeUsb = 7;

    public IReadOnlyList<UsbDiskInfo> GetUsbDisks()
    {
        var scope = new ManagementScope(StorageNamespace);
        scope.Connect();

        var appDrive = char.ToUpperInvariant(Path.GetPathRoot(AppContext.BaseDirectory)?.FirstOrDefault() ?? '\0');
        var result = new List<UsbDiskInfo>();

        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(
            "SELECT Number, FriendlyName, Size, BusType, IsSystem, IsBoot, PartitionStyle, UniqueId, " +
            "SerialNumber, IsReadOnly, IsOffline FROM MSFT_Disk"));

        foreach (var obj in searcher.Get())
        {
            using var disk = (ManagementObject)obj;

            if (ToUShort(disk["BusType"]) != BusTypeUsb) continue;
            // Hard safety rule: never offer the Windows system or boot disk.
            if (ToBool(disk["IsSystem"]) || ToBool(disk["IsBoot"])) continue;

            var number = Convert.ToUInt32(disk["Number"]);
            var partitions = GetPartitions(scope, number);

            string? protectedReason = null;
            if (appDrive != '\0' && partitions.Any(p => p.DriveLetter == appDrive))
                protectedReason = $"UsbMaker is running from drive {appDrive}: on this disk.";

            char? dataLetter = null, bootLetter = null;
            foreach (var p in partitions.Where(p => p.DriveLetter != null))
            {
                var root = $"{p.DriveLetter}:\\";
                if (SafeFileExists(Path.Combine(root, Constants.DataMarkerFile))) dataLetter = p.DriveLetter;
                if (SafeFileExists(Path.Combine(root, Constants.BootMarkerFile)) ||
                    string.Equals(p.VolumeLabel, Constants.BootVolumeLabel, StringComparison.OrdinalIgnoreCase))
                    bootLetter = p.DriveLetter;
            }

            result.Add(new UsbDiskInfo
            {
                Number = number,
                FriendlyName = (disk["FriendlyName"] as string ?? "USB Disk").Trim(),
                SizeBytes = Convert.ToUInt64(disk["Size"] ?? 0UL),
                PartitionStyle = ToUShort(disk["PartitionStyle"]) switch { 1 => "MBR", 2 => "GPT", _ => "RAW" },
                UniqueId = disk["UniqueId"] as string ?? "",
                SerialNumber = disk["SerialNumber"] as string ?? "",
                IsReadOnly = ToBool(disk["IsReadOnly"]),
                IsOffline = ToBool(disk["IsOffline"]),
                Partitions = partitions,
                ProtectedReason = protectedReason,
                CpDataLetter = dataLetter,
                CpBootLetter = bootLetter
            });
        }

        return result.OrderBy(d => d.Number).ToList();
    }

    /// <summary>Re-reads one disk; used right before erasing to make sure it is still the same device.</summary>
    public UsbDiskInfo? FindByNumber(uint number) => GetUsbDisks().FirstOrDefault(d => d.Number == number);

    private static List<PartitionInfo> GetPartitions(ManagementScope scope, uint diskNumber)
    {
        var list = new List<PartitionInfo>();
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(
            $"SELECT PartitionNumber, DriveLetter, Size FROM MSFT_Partition WHERE DiskNumber = {diskNumber}"));

        foreach (var obj in searcher.Get())
        {
            using var part = (ManagementObject)obj;
            char? letter = null;
            var raw = part["DriveLetter"];
            if (raw != null)
            {
                var c = Convert.ToChar(raw);
                if (char.IsLetter(c)) letter = char.ToUpperInvariant(c);
            }

            string? label = null, fs = null;
            if (letter != null)
            {
                try
                {
                    var di = new DriveInfo(letter.Value.ToString());
                    if (di.IsReady) { label = di.VolumeLabel; fs = di.DriveFormat; }
                }
                catch { /* unformatted or busy volume */ }
            }

            list.Add(new PartitionInfo
            {
                PartitionNumber = Convert.ToUInt32(part["PartitionNumber"]),
                DriveLetter = letter,
                SizeBytes = Convert.ToUInt64(part["Size"] ?? 0UL),
                VolumeLabel = label,
                FileSystem = fs
            });
        }
        return list.OrderBy(p => p.PartitionNumber).ToList();
    }

    private static bool SafeFileExists(string path)
    {
        try { return File.Exists(path); } catch { return false; }
    }

    private static ushort ToUShort(object? o) => o == null ? (ushort)0 : Convert.ToUInt16(o);
    private static bool ToBool(object? o) => o is bool b && b;
}
