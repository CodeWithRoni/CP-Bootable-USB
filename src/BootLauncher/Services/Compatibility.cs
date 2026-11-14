using BootLauncher.Models;

namespace BootLauncher.Services
{
    /// <summary>
    /// Decides the install engine for an edition and whether it can be installed on this PC
    /// in the current boot mode. These are Windows' own rules — we only report them clearly.
    /// </summary>
    public static class Compatibility
    {
        public static void Evaluate(ImageEdition e, HardwareInfo hw, bool forceDirectApply)
        {
            var isX86 = e.Arch.Equals("x86", System.StringComparison.OrdinalIgnoreCase);
            var isX64 = e.Arch.Equals("x64", System.StringComparison.OrdinalIgnoreCase);
            e.BlockedReason = null;
            e.Warning = null;

            // ---- engine
            //  • 32-bit images: x64 WinPE has no WOW64, so their setup.exe can't run → DISM apply.
            //  • Windows 7: its setup.exe is unreliable inside a Windows 10/11-based WinPE → DISM apply.
            //  • 64-bit image while booted into 32-bit WinPE: x64 setup.exe can't run → DISM apply.
            var direct = forceDirectApply
                         || e.Family == WindowsFamily.Win7
                         || (isX86 && hw.PeIs64Bit)
                         || (isX64 && !hw.PeIs64Bit);
            e.Engine = direct ? InstallEngine.DirectApply : InstallEngine.Setup;

            // ---- hard blocks
            if (isX64 && !hw.CpuIs64BitCapable)
            {
                e.BlockedReason = "This PC's CPU is 32-bit only. Choose a 32-bit (x86) Windows image.";
                return;
            }
            if (!(isX86 || isX64))
            {
                e.BlockedReason = $"Architecture '{e.Arch}' is not supported on this PC.";
                return;
            }
            if (isX86 && hw.IsUefi)
            {
                e.BlockedReason = "32-bit Windows cannot be installed in UEFI mode. Restart, enable Legacy/CSM boot in BIOS, " +
                                  "and boot this USB in Legacy mode.";
                return;
            }

            // ---- warnings
            if (e.Family == WindowsFamily.Win7 && hw.IsUefi)
            {
                e.Warning = "Windows 7 in UEFI mode needs CSM enabled and Secure Boot OFF, otherwise it may hang at \"Starting Windows\". " +
                            "Legacy mode is recommended for Windows 7.";
                if (hw.SecureBoot == true)
                    e.Warning = "Secure Boot is ON — Windows 7 will not start. Turn Secure Boot off in BIOS first (or use Legacy mode).";
            }
            else if (e.Family == WindowsFamily.Win7)
            {
                e.Warning = "Windows 7 has no USB 3.0 / NVMe drivers. Put them in Drivers\\Win7\\" + (isX86 ? "x86" : "x64") +
                            " on the USB so they are added automatically.";
            }
            else if (e.Family == WindowsFamily.Win11 && e.Engine == InstallEngine.Setup && (!hw.IsUefi || hw.SecureBoot == false))
            {
                e.Warning = "This PC may not meet Windows 11 requirements (TPM 2.0 / Secure Boot). Use the bypass options below if Setup refuses.";
            }
            else if (e.Family == WindowsFamily.Win11 && hw.RamBytes > 0 && hw.RamBytes < 4UL * 1024 * 1024 * 1024)
            {
                e.Warning = "Less than 4 GB RAM — tick \"Bypass RAM check\" for Windows 11.";
            }
        }
    }
}
