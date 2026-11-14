using System;
using System.Collections.Generic;

namespace CP.Shared.Models
{
    /// <summary>Built-in colour themes. "DarkBlueCyan" is the COMPUTER PLUS default.</summary>
    public static class ThemePresets
    {
        public const string Custom = "Custom";

        private static readonly Dictionary<string, ThemeSection> Presets =
            new Dictionary<string, ThemeSection>(StringComparer.OrdinalIgnoreCase)
            {
                ["DarkBlueCyan"] = Make("DarkBlueCyan", "#0F172A", "#1E293B", "#0284C7", "#38BDF8"),
                ["DarkEmerald"]  = Make("DarkEmerald",  "#0F172A", "#1E293B", "#059669", "#34D399"),
                ["DarkPurple"]   = Make("DarkPurple",   "#0F172A", "#1E293B", "#7C3AED", "#A78BFA"),
                ["DarkOrange"]   = Make("DarkOrange",   "#0F172A", "#1E293B", "#EA580C", "#FB923C"),
                ["DarkRed"]      = Make("DarkRed",      "#0F172A", "#1E293B", "#DC2626", "#F87171"),
            };

        public static IEnumerable<string> Names => Presets.Keys;

        public static bool Exists(string? name) => name != null && Presets.ContainsKey(name);

        /// <summary>Returns a copy of the preset (or DarkBlueCyan if the name is unknown).</summary>
        public static ThemeSection Get(string? name)
        {
            var p = name != null && Presets.TryGetValue(name, out var found) ? found : Presets["DarkBlueCyan"];
            return Clone(p);
        }

        public static ThemeSection Clone(ThemeSection t) => new ThemeSection
        {
            Preset = t.Preset,
            Background = t.Background,
            Surface = t.Surface,
            Accent = t.Accent,
            AccentLight = t.AccentLight,
            TextPrimary = t.TextPrimary,
            TextMuted = t.TextMuted
        };

        private static ThemeSection Make(string name, string bg, string surface, string accent, string accentLight) =>
            new ThemeSection
            {
                Preset = name,
                Background = bg,
                Surface = surface,
                Accent = accent,
                AccentLight = accentLight,
                TextPrimary = "#F1F5F9",
                TextMuted = "#94A3B8"
            };
    }
}
