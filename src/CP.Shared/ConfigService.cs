using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP.Shared.Models;

namespace CP.Shared
{
    /// <summary>Result of loading config.json. Config is always fully populated.</summary>
    public sealed class ConfigLoadResult
    {
        public ConfigLoadResult(CpConfig config, bool loadedFromFile, IReadOnlyList<string> warnings)
        {
            Config = config;
            LoadedFromFile = loadedFromFile;
            Warnings = warnings;
        }

        public CpConfig Config { get; }
        public bool LoadedFromFile { get; }
        public IReadOnlyList<string> Warnings { get; }
    }

    /// <summary>
    /// Reads/writes config.json. Loading never throws: any missing or invalid value is
    /// replaced with the COMPUTER PLUS default so the boot screen can never come up blank.
    /// </summary>
    public static class ConfigService
    {
        private static readonly Regex HexColor = new Regex("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);

        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // Keep Bangla / Unicode text human-readable in the file.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static bool IsValidColor(string? value) => value != null && HexColor.IsMatch(value.Trim());

        public static ConfigLoadResult Load(string path)
        {
            var warnings = new List<string>();
            CpConfig? raw = null;
            var fromFile = false;

            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path, Encoding.UTF8);
                    raw = JsonSerializer.Deserialize<CpConfig>(json, JsonOptions);
                    fromFile = raw != null;
                }
                else
                {
                    warnings.Add($"config.json not found at '{path}', using built-in defaults.");
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"config.json could not be read ({ex.Message}), using built-in defaults.");
            }

            return new ConfigLoadResult(Normalize(raw, warnings), fromFile, warnings);
        }

        public static ConfigLoadResult LoadFromJson(string json)
        {
            var warnings = new List<string>();
            CpConfig? raw = null;
            try { raw = JsonSerializer.Deserialize<CpConfig>(json, JsonOptions); }
            catch (Exception ex) { warnings.Add($"Invalid JSON ({ex.Message}), using built-in defaults."); }
            return new ConfigLoadResult(Normalize(raw, warnings), raw != null, warnings);
        }

        public static string Serialize(CpConfig config) => JsonSerializer.Serialize(Normalize(config, null), JsonOptions);

        /// <summary>Writes atomically (temp file + replace) so a pulled USB can't leave a half-written config.</summary>
        public static void Save(CpConfig config, string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, Serialize(config), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>Returns a new config where every null/blank/invalid value is replaced by its default.</summary>
        public static CpConfig Normalize(CpConfig? input, List<string>? warnings)
        {
            var d = BrandingDefaults.Create();
            var c = input ?? new CpConfig();

            var b = c.Branding ?? new BrandingSection();
            var contact = c.Contact ?? new ContactSection();
            var launcher = c.Launcher ?? new LauncherSection();
            var install = c.Install ?? new InstallSection();

            var phones = (contact.Phones ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .ToList();

            var isoFolders = (launcher.IsoFolders ?? new List<string>())
                .Where(f => f != null)
                .Select(f => f.Trim().Trim('\\', '/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new CpConfig
            {
                SchemaVersion = Constants.ConfigSchemaVersion,
                Branding = new BrandingSection
                {
                    SoftwareName = Pick(b.SoftwareName, d.Branding!.SoftwareName),
                    BrandName = Pick(b.BrandName, d.Branding.BrandName),
                    Tagline = Pick(b.Tagline, d.Branding.Tagline),
                    DeveloperCredit = Pick(b.DeveloperCredit, d.Branding.DeveloperCredit),
                    LogoPath = string.IsNullOrWhiteSpace(b.LogoPath) ? null : b.LogoPath!.Trim()
                },
                Contact = new ContactSection
                {
                    Phones = phones.Count > 0 ? phones : d.Contact!.Phones,
                    Address = Pick(contact.Address, d.Contact!.Address)
                },
                Notice = Pick(c.Notice, d.Notice),
                Theme = NormalizeTheme(c.Theme, warnings),
                Launcher = new LauncherSection
                {
                    IsoFolders = isoFolders.Count > 0 ? isoFolders : d.Launcher!.IsoFolders,
                    ScanAllDrives = launcher.ScanAllDrives ?? d.Launcher!.ScanAllDrives,
                    DefaultBypass = launcher.DefaultBypass ?? new BypassSection(),
                    ShowCommandPrompt = launcher.ShowCommandPrompt ?? d.Launcher!.ShowCommandPrompt,
                    ConfirmBeforeInstall = launcher.ConfirmBeforeInstall ?? d.Launcher!.ConfirmBeforeInstall
                },
                Install = new InstallSection
                {
                    ForceDirectApply = install.ForceDirectApply ?? d.Install!.ForceDirectApply,
                    DriversFolder = Pick(install.DriversFolder, d.Install!.DriversFolder),
                    InjectDriversForWin7 = install.InjectDriversForWin7 ?? d.Install.InjectDriversForWin7,
                    AutoReboot = install.AutoReboot ?? d.Install.AutoReboot
                },
                Meta = new MetaSection { CreatedBy = c.Meta?.CreatedBy, CreatedAt = c.Meta?.CreatedAt }
            };
        }

        private static ThemeSection NormalizeTheme(ThemeSection? t, List<string>? warnings)
        {
            // Start from the named preset (or default), then overlay any valid custom colours.
            var baseTheme = ThemePresets.Get(ThemePresets.Exists(t?.Preset) ? t!.Preset : BrandingDefaults.ThemePreset);
            if (t == null) return baseTheme;

            var preset = string.IsNullOrWhiteSpace(t.Preset) ? baseTheme.Preset : t.Preset!.Trim();
            return new ThemeSection
            {
                Preset = preset,
                Background = Color(t.Background, baseTheme.Background!, "background", warnings),
                Surface = Color(t.Surface, baseTheme.Surface!, "surface", warnings),
                Accent = Color(t.Accent, baseTheme.Accent!, "accent", warnings),
                AccentLight = Color(t.AccentLight, baseTheme.AccentLight!, "accentLight", warnings),
                TextPrimary = Color(t.TextPrimary, baseTheme.TextPrimary!, "textPrimary", warnings),
                TextMuted = Color(t.TextMuted, baseTheme.TextMuted!, "textMuted", warnings)
            };
        }

        private static string Color(string? value, string fallback, string name, List<string>? warnings)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (IsValidColor(value)) return value!.Trim().ToUpperInvariant();
            warnings?.Add($"theme.{name} '{value}' is not a valid #RRGGBB colour, using {fallback}.");
            return fallback;
        }

        private static string Pick(string? value, string? fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback ?? string.Empty : value!.Trim();
    }
}
