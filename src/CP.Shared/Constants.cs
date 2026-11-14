namespace CP.Shared
{
    /// <summary>
    /// Fixed names shared by UsbMaker and BootLauncher. Changing any of these breaks
    /// compatibility with USB drives created by older versions, so treat them as a contract.
    /// </summary>
    public static class Constants
    {
        public const int ConfigSchemaVersion = 1;

        // Volume labels (FAT32 labels are limited to 11 characters).
        public const string BootVolumeLabel = "CP_WIN_BOOT";
        public const string DataVolumeLabel = "CP_WIN_DATA";

        // Marker files used to find the partitions regardless of drive letter.
        public const string BootMarkerFile = "cp_boot.tag";            // root of BOOT partition
        public const string DataMarkerFile = @"CP\cp_data.tag";         // on DATA partition

        // DATA partition layout.
        public const string ConfigFileName = "config.json";             // root of DATA partition
        public const string AppFolder = "CP";
        public const string LogsFolder = @"CP\logs";
        public const string IsoFolder = "ISO";
        public const string DriversFolder = "Drivers";
        public const string Win7DriversX86 = @"Drivers\Win7\x86";
        public const string Win7DriversX64 = @"Drivers\Win7\x64";
        public const string LogoFileBaseName = @"CP\logo";              // extension preserved (.png/.jpg)

        // BOOT partition.
        public const int DefaultBootPartitionSizeMb = 1024;
        public const string BootWimRelativePath = @"sources\boot.wim";        // 64-bit WinPE (BIOS + UEFI)
        public const string BootWimX86RelativePath = @"sources\boot_x86.wim"; // optional 32-bit WinPE (BIOS only)
        /// <summary>Recommended boot partition size when the 32-bit WinPE is included.</summary>
        public const int BootPartitionSizeWithX86Mb = 1536;
        public const string BiosBcdRelativePath = @"Boot\BCD";
        public const string UefiBcdRelativePath = @"EFI\Microsoft\Boot\BCD";
    }
}
