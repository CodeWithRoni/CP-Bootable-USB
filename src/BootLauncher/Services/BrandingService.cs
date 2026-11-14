using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CP.Shared;
using CP.Shared.Models;

namespace BootLauncher.Services
{
    /// <summary>Finds the CP_WIN_DATA partition, loads config.json and applies the colour theme.</summary>
    public static class BrandingService
    {
        /// <summary>Root of the CP_WIN_DATA partition (e.g. "D:\"), or null if not found.</summary>
        public static string? DataRoot { get; private set; }

        public static CpConfig Config { get; private set; } = BrandingDefaults.Create();

        public static string? FindDataRoot()
        {
            var system = Path.GetPathRoot(Environment.SystemDirectory);
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); } catch { return null; }

            // 1st choice: marker file. 2nd choice: volume label.
            foreach (var d in drives)
            {
                try
                {
                    if (!d.IsReady || string.Equals(d.Name, system, StringComparison.OrdinalIgnoreCase)) continue;
                    if (File.Exists(Path.Combine(d.RootDirectory.FullName, Constants.DataMarkerFile))) return d.RootDirectory.FullName;
                }
                catch { }
            }
            foreach (var d in drives)
            {
                try
                {
                    if (d.IsReady && string.Equals(d.VolumeLabel, Constants.DataVolumeLabel, StringComparison.OrdinalIgnoreCase))
                        return d.RootDirectory.FullName;
                }
                catch { }
            }
            return null;
        }

        public static void Load()
        {
            DataRoot = FindDataRoot();
            Log.UseDataRoot(DataRoot);

            if (DataRoot == null)
            {
                Log.Warn("CP_WIN_DATA partition not found — using built-in COMPUTER PLUS branding.");
                Config = BrandingDefaults.Create();
                return;
            }

            Log.Info("CP_WIN_DATA found at " + DataRoot);
            var result = ConfigService.Load(Path.Combine(DataRoot, Constants.ConfigFileName));
            foreach (var w in result.Warnings) Log.Warn(w);
            Config = result.Config;
        }

        /// <summary>Replaces the theme brushes (used via DynamicResource everywhere).</summary>
        public static void ApplyTheme(ResourceDictionary resources)
        {
            var t = Config.Theme ?? ThemePresets.Get(null);
            Set(resources, "BgBrush", t.Background);
            Set(resources, "SurfaceBrush", t.Surface);
            Set(resources, "AccentBrush", t.Accent);
            Set(resources, "AccentLightBrush", t.AccentLight);
            Set(resources, "TextBrush", t.TextPrimary);
            Set(resources, "MutedBrush", t.TextMuted);

            try
            {
                var accent = (Color)ColorConverter.ConvertFromString(t.Accent);
                var light = (Color)ColorConverter.ConvertFromString(t.AccentLight);
                var g = new LinearGradientBrush(accent, light, 0);
                g.Freeze();
                resources["AccentGradient"] = g;
            }
            catch { }
        }

        private static void Set(ResourceDictionary r, string key, string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return;
            try
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                b.Freeze();
                r[key] = b;
            }
            catch { }
        }

        /// <summary>Custom logo from the USB, or the built-in COMPUTER PLUS shop logo.</summary>
        public static ImageSource LoadLogo()
        {
            var rel = Config.Branding?.LogoPath;
            if (DataRoot != null && !string.IsNullOrWhiteSpace(rel))
            {
                var path = Path.Combine(DataRoot, rel);
                if (File.Exists(path))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(path, UriKind.Absolute);
                        bmp.DecodePixelHeight = 256;
                        bmp.EndInit();
                        bmp.Freeze();
                        return bmp;
                    }
                    catch (Exception ex) { Log.Warn("Logo could not be loaded: " + ex.Message); }
                }
            }
            var builtIn = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png", UriKind.Absolute));
            builtIn.Freeze();
            return builtIn;
        }

        public static string PhonesText =>
            string.Join("   |   ", (Config.Contact?.Phones ?? BrandingDefaults.Phones.ToList()));
    }
}
