using System.Collections.Generic;

namespace CP.Shared.Models
{
    /// <summary>Root of config.json (schema v1). Every member is optional; ConfigService fills gaps with defaults.</summary>
    public sealed class CpConfig
    {
        public int SchemaVersion { get; set; } = Constants.ConfigSchemaVersion;
        public BrandingSection? Branding { get; set; }
        public ContactSection? Contact { get; set; }
        public string? Notice { get; set; }
        public ThemeSection? Theme { get; set; }
        public LauncherSection? Launcher { get; set; }
        public InstallSection? Install { get; set; }
        public MetaSection? Meta { get; set; }
    }

    public sealed class BrandingSection
    {
        public string? SoftwareName { get; set; }
        public string? BrandName { get; set; }
        public string? Tagline { get; set; }
        public string? DeveloperCredit { get; set; }
        /// <summary>Path relative to the DATA partition root, e.g. "CP\logo.png". Null/empty = built-in logo.</summary>
        public string? LogoPath { get; set; }
    }

    public sealed class ContactSection
    {
        public List<string>? Phones { get; set; }
        public string? Address { get; set; }
    }

    public sealed class ThemeSection
    {
        public string? Preset { get; set; }
        public string? Background { get; set; }
        public string? Surface { get; set; }
        public string? Accent { get; set; }
        public string? AccentLight { get; set; }
        public string? TextPrimary { get; set; }
        public string? TextMuted { get; set; }
    }

    public sealed class LauncherSection
    {
        /// <summary>Folders (relative to each drive root) scanned for ISO files. "" means the drive root.</summary>
        public List<string>? IsoFolders { get; set; }
        public bool? ScanAllDrives { get; set; }
        public BypassSection? DefaultBypass { get; set; }
        public bool? ShowCommandPrompt { get; set; }
        public bool? ConfirmBeforeInstall { get; set; }
    }

    public sealed class BypassSection
    {
        public bool Tpm { get; set; }
        public bool SecureBoot { get; set; }
        public bool Ram { get; set; }
        public bool Cpu { get; set; }
        public bool Storage { get; set; }
    }

    public sealed class InstallSection
    {
        /// <summary>Use the DISM "Direct Apply" engine for every ISO, not only x86 / Windows 7.</summary>
        public bool? ForceDirectApply { get; set; }
        public string? DriversFolder { get; set; }
        public bool? InjectDriversForWin7 { get; set; }
        public bool? AutoReboot { get; set; }
    }

    public sealed class MetaSection
    {
        public string? CreatedBy { get; set; }
        public string? CreatedAt { get; set; }
    }
}
