using System;
using System.Runtime.InteropServices;

namespace BootLauncher.Services
{
    /// <summary>
    /// WinPE's basic display driver sometimes starts at 800×600 on Legacy BIOS PCs. If the screen is
    /// smaller than 1024×768, switch to the smallest available 32-bit mode that is at least 1024×768.
    /// Larger (native UEFI) resolutions are never touched.
    /// </summary>
    public static class DisplayHelper
    {
        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int DM_BITSPERPEL = 0x40000, DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000;
        private const int DISP_CHANGE_SUCCESSFUL = 0;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);

        public static void EnsureMinimumResolution()
        {
            try
            {
                var current = New();
                if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref current)) return;
                if (current.dmPelsWidth >= 1024 && current.dmPelsHeight >= 768) return;

                DEVMODE? best = null;
                var m = New();
                for (var i = 0; EnumDisplaySettings(null, i, ref m); i++)
                {
                    if (m.dmPelsWidth >= 1024 && m.dmPelsHeight >= 768 && m.dmBitsPerPel >= 16 &&
                        (best == null || (long)m.dmPelsWidth * m.dmPelsHeight < (long)best.Value.dmPelsWidth * best.Value.dmPelsHeight ||
                         (m.dmPelsWidth == best.Value.dmPelsWidth && m.dmBitsPerPel > best.Value.dmBitsPerPel)))
                        best = m;
                    m = New();
                }
                if (best == null) { Log.Warn($"Screen is {current.dmPelsWidth}x{current.dmPelsHeight}; no 1024x768 mode available."); return; }

                var target = best.Value;
                target.dmFields = DM_BITSPERPEL | DM_PELSWIDTH | DM_PELSHEIGHT;
                var r = ChangeDisplaySettings(ref target, 0);
                Log.Info($"Display {current.dmPelsWidth}x{current.dmPelsHeight} → {target.dmPelsWidth}x{target.dmPelsHeight}: " +
                         (r == DISP_CHANGE_SUCCESSFUL ? "OK" : "failed (" + r + ")"));
            }
            catch (Exception ex) { Log.Warn("Display: " + ex.Message); }
        }

        private static DEVMODE New()
        {
            var d = new DEVMODE { dmDeviceName = "", dmFormName = "" };
            d.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            return d;
        }
    }
}
