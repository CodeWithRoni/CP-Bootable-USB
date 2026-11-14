using System;
using System.Collections.Generic;
using System.Linq;

namespace BootLauncher.Models
{
    public static class Format
    {
        public static string Bytes(ulong bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            var i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return $"{v:0.#} {u[i]}";
        }
    }

    public sealed class HardwareInfo
    {
        public string CpuName { get; set; } = "Unknown CPU";
        public string CpuCores { get; set; } = "";
        public bool CpuIs64BitCapable { get; set; } = true;
        public ulong RamBytes { get; set; }
        public string Model { get; set; } = "";
        public bool IsUefi { get; set; }
        public bool? SecureBoot { get; set; }
        public bool PeIs64Bit { get; set; }
        public bool IsWinPE { get; set; }
        public List<TargetDisk> Disks { get; set; } = new List<TargetDisk>();

        public string RamText => RamBytes == 0 ? "Unknown" : Format.Bytes(RamBytes);
        public string BootModeText => IsUefi ? "UEFI (GPT)" : "Legacy BIOS (MBR)";
        public string SecureBootText => SecureBoot == null ? (IsUefi ? "Unknown" : "N/A (Legacy)") : (SecureBoot.Value ? "On" : "Off");
        public string EnvironmentText => (IsWinPE ? "WinPE " : "Windows (test mode) ") + (PeIs64Bit ? "64-bit" : "32-bit");
    }

    public sealed class TargetDisk
    {
        public int Index { get; set; }
        public string Model { get; set; } = "";
        public ulong SizeBytes { get; set; }
        public string InterfaceType { get; set; } = "";
        public string MediaType { get; set; } = "";

        /// <summary>Why this disk can't be used as install target (USB stick itself, ISO source...).</summary>
        public string? BlockedReason { get; set; }
        public bool IsBlocked => BlockedReason != null;

        public string SizeText => Format.Bytes(SizeBytes);
        public string Title => $"Disk {Index}: {Model}";
        public string Subtitle
        {
            get
            {
                var s = $"{SizeText}  ·  {InterfaceType}";
                if (!string.IsNullOrEmpty(MediaType)) s += "  ·  " + MediaType;
                return s;
            }
        }
        public string ShortText => $"Disk {Index}: {Model} ({SizeText})";
        public override string ToString() => ShortText;
    }

    public enum WindowsFamily { Unknown, Win7, Win8, Win81, Win10, Win11 }

    public enum InstallEngine { Setup, DirectApply }

    public static class FamilyText
    {
        public static string Name(WindowsFamily f)
        {
            switch (f)
            {
                case WindowsFamily.Win7: return "Windows 7";
                case WindowsFamily.Win8: return "Windows 8";
                case WindowsFamily.Win81: return "Windows 8.1";
                case WindowsFamily.Win10: return "Windows 10";
                case WindowsFamily.Win11: return "Windows 11";
                default: return "Windows";
            }
        }

        /// <summary>Maps an image version (e.g. 10.0.22631) to a Windows family.</summary>
        public static WindowsFamily FromVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version)) return WindowsFamily.Unknown;
            var parts = version!.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
            if (parts.Length < 2) return WindowsFamily.Unknown;
            if (parts[0] == 6 && parts[1] == 1) return WindowsFamily.Win7;
            if (parts[0] == 6 && parts[1] == 2) return WindowsFamily.Win8;
            if (parts[0] == 6 && parts[1] == 3) return WindowsFamily.Win81;
            if (parts[0] == 10) return parts.Length > 2 && parts[2] >= 22000 ? WindowsFamily.Win11 : WindowsFamily.Win10;
            return WindowsFamily.Unknown;
        }
    }

    /// <summary>One ISO file found on a drive.</summary>
    public sealed class IsoItem
    {
        public string Path { get; set; } = "";
        public string FileName => System.IO.Path.GetFileName(Path);
        public ulong SizeBytes { get; set; }
        public string SizeText => Format.Bytes(SizeBytes);
        public string VolumeLabel { get; set; } = "";
        public WindowsFamily GuessFamily { get; set; }
        public string GuessArch { get; set; } = "";
        public string Location => System.IO.Path.GetDirectoryName(Path) ?? "";

        public string GuessText
        {
            get
            {
                var parts = new List<string>();
                if (GuessFamily != WindowsFamily.Unknown) parts.Add(FamilyText.Name(GuessFamily));
                if (!string.IsNullOrEmpty(GuessArch)) parts.Add(GuessArch);
                return parts.Count == 0 ? "Windows ISO" : string.Join(" · ", parts);
            }
        }
    }

    /// <summary>One installable edition inside an ISO (install.wim / install.esd index).</summary>
    public sealed class ImageEdition
    {
        public string ImageFile { get; set; } = "";
        public string SetupExe { get; set; } = "";
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public string Arch { get; set; } = "";
        public string Version { get; set; } = "";
        public WindowsFamily Family { get; set; }
        public InstallEngine Engine { get; set; }
        public string? BlockedReason { get; set; }
        public string? Warning { get; set; }

        public bool IsBlocked => BlockedReason != null;
        public string FamilyName => FamilyText.Name(Family);
        public string EngineText => Engine == InstallEngine.Setup ? "Windows Setup" : "Direct Apply";
        public string Details => $"{FamilyName} · {Arch} · {Version}";
        public override string ToString() => $"{Index}: {Name}";
    }
}
