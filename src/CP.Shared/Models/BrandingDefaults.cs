using System.Collections.Generic;

namespace CP.Shared.Models
{
    /// <summary>
    /// Hardcoded COMPUTER PLUS defaults. Used when config.json is missing, unreadable,
    /// or when an individual field is blank/invalid.
    /// </summary>
    public static class BrandingDefaults
    {
        public const string SoftwareName = "CP Bootable USB";
        public const string BrandName = "COMPUTER PLUS";
        public const string Tagline = "Fast & Reliable Windows Deployment - We Always Ready To Help You.";
        public const string DeveloperCredit = "Developed by MD AMZAD HOSSAIN RONI";
        public const string Address = "Fokir Plaza, Ground Floor, Shop No #04, Bera, Pabna";

        public static readonly IReadOnlyList<string> Phones = new[] { "+8801754 614 614", "+8801913 354 321" };

        public const string Notice =
            "Welcome to COMPUTER PLUS Deployment Environment. Please select your desired Windows ISO image " +
            "from the list on the right and click 'Install Windows'. Ensure your important data is backed up " +
            "before formatting drives. For technical support, contact COMPUTER PLUS.";

        public const string ThemePreset = "DarkBlueCyan";

        /// <summary>Builds a fully populated config with every default filled in.</summary>
        public static CpConfig Create() => new CpConfig
        {
            SchemaVersion = Constants.ConfigSchemaVersion,
            Branding = new BrandingSection
            {
                SoftwareName = SoftwareName,
                BrandName = BrandName,
                Tagline = Tagline,
                DeveloperCredit = DeveloperCredit,
                LogoPath = null
            },
            Contact = new ContactSection { Phones = new List<string>(Phones), Address = Address },
            Notice = Notice,
            Theme = ThemePresets.Get(ThemePreset),
            Launcher = new LauncherSection
            {
                IsoFolders = new List<string> { Constants.IsoFolder, "" },
                ScanAllDrives = true,
                DefaultBypass = new BypassSection(),
                ShowCommandPrompt = true,
                ConfirmBeforeInstall = true
            },
            Install = new InstallSection
            {
                ForceDirectApply = false,
                DriversFolder = Constants.DriversFolder,
                InjectDriversForWin7 = true,
                AutoReboot = true
            },
            Meta = new MetaSection()
        };
    }
}
