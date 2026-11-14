using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using BootLauncher.Models;

namespace BootLauncher.Services
{
    /// <summary>Hardware + environment information (WinPE-WMI).</summary>
    public static class SystemInfo
    {
        /// <summary>True when running inside WinPE (MiniNT key present).</summary>
        public static bool IsWinPE
        {
            get
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\MiniNT"))
                        return k != null;
                }
                catch { return false; }
            }
        }

        public static bool IsUefi
        {
            get
            {
                // WinPE: PEFirmwareType 1 = BIOS, 2 = UEFI.
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control"))
                    {
                        if (k?.GetValue("PEFirmwareType") is int fw) return fw == 2;
                    }
                }
                catch { }

                // Normal Windows (testing): kernel32!GetFirmwareType.
                try { return GetFirmwareType(out var t) && t == 2; }
                catch { return false; }
            }
        }

        public static bool? SecureBootEnabled
        {
            get
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State"))
                    {
                        if (k?.GetValue("UEFISecureBootEnabled") is int v) return v == 1;
                    }
                }
                catch { }
                return null;
            }
        }

        public static bool PeIs64Bit => Environment.Is64BitOperatingSystem;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFirmwareType(out uint firmwareType);

        public static HardwareInfo ReadHardware()
        {
            var hw = new HardwareInfo
            {
                IsUefi = IsUefi,
                SecureBoot = SecureBootEnabled,
                PeIs64Bit = PeIs64Bit,
                IsWinPE = IsWinPE
            };

            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, DataWidth, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
                    foreach (ManagementObject o in s.Get())
                    {
                        hw.CpuName = Regex.Replace((o["Name"] as string ?? "Unknown CPU").Trim(), @"\s+", " ");
                        hw.CpuIs64BitCapable = Convert.ToInt32(o["DataWidth"] ?? 64) == 64;
                        hw.CpuCores = $"{o["NumberOfCores"]} cores / {o["NumberOfLogicalProcessors"]} threads";
                        break;
                    }
            }
            catch (Exception ex) { Log.Warn("CPU info: " + ex.Message); }

            try
            {
                using (var s = new ManagementObjectSearcher("SELECT TotalPhysicalMemory, Manufacturer, Model FROM Win32_ComputerSystem"))
                    foreach (ManagementObject o in s.Get())
                    {
                        hw.RamBytes = Convert.ToUInt64(o["TotalPhysicalMemory"] ?? 0UL);
                        hw.Model = $"{o["Manufacturer"]} {o["Model"]}".Trim();
                        break;
                    }
            }
            catch (Exception ex) { Log.Warn("RAM info: " + ex.Message); }

            hw.Disks = GetDisks();
            return hw;
        }

        public static List<TargetDisk> GetDisks()
        {
            var list = new List<TargetDisk>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Index, Model, Size, InterfaceType, MediaType FROM Win32_DiskDrive"))
                    foreach (ManagementObject o in s.Get())
                    {
                        list.Add(new TargetDisk
                        {
                            Index = Convert.ToInt32(o["Index"]),
                            Model = (o["Model"] as string ?? "Disk").Trim(),
                            SizeBytes = Convert.ToUInt64(o["Size"] ?? 0UL),
                            InterfaceType = (o["InterfaceType"] as string ?? "").Trim(),
                            MediaType = (o["MediaType"] as string ?? "").Trim()
                        });
                    }
            }
            catch (Exception ex) { Log.Warn("Disk info: " + ex.Message); }
            return list.OrderBy(d => d.Index).ToList();
        }

        /// <summary>Physical disk index that holds a drive letter (e.g. the USB, or the drive with the ISO).</summary>
        public static int? DiskIndexOfDrive(string? pathOrRoot)
        {
            if (string.IsNullOrEmpty(pathOrRoot)) return null;
            var root = Path.GetPathRoot(pathOrRoot);
            if (string.IsNullOrEmpty(root) || root!.Length < 2) return null;
            var letter = root.Substring(0, 2).ToUpperInvariant(); // "D:"
            try
            {
                var q = $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{letter}'}} WHERE AssocClass=Win32_LogicalDiskToPartition";
                using (var s = new ManagementObjectSearcher(q))
                    foreach (ManagementObject p in s.Get())
                        return Convert.ToInt32(p["DiskIndex"]);
            }
            catch (Exception ex) { Log.Warn($"Disk lookup for {letter}: {ex.Message}"); }
            return null;
        }
    }
}
