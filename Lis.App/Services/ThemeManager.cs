using System;
using System.Windows;
using System.Windows.Media;

namespace Lis.App.Services
{
    public static class ThemeManager
    {
        // ── Theme name constants ──────────────────────────────────────────
        public const string ThemeCleanWhite  = "CleanWhite";   // Medical Clean White
        public const string ThemeBiotechDark = "BiotechDark";  // Biotech Dark Mode
        public const string ThemeRoyalBlue   = "RoyalBlue";    // Clinical Royal Blue

        // Legacy aliases kept for compatibility (database may store old values)
        public const string ThemeNight = ThemeBiotechDark;
        public const string ThemeLight = ThemeCleanWhite;

        public static string CurrentTheme { get; private set; } = ThemeBiotechDark;

        public static event Action<string>? ThemeChanged;

        public static void ApplyTheme(string? themeName)
        {
            if (string.IsNullOrWhiteSpace(themeName))
                themeName = ThemeBiotechDark;

            themeName = themeName.Trim();

            // Normalise legacy / alias values
            if (themeName.Equals("Light", StringComparison.OrdinalIgnoreCase) ||
                themeName.Contains("light", StringComparison.OrdinalIgnoreCase) ||
                themeName.Contains("نهار", StringComparison.OrdinalIgnoreCase) ||
                themeName.Equals(ThemeCleanWhite, StringComparison.OrdinalIgnoreCase))
            {
                ApplyCleanWhiteTheme();
                CurrentTheme = ThemeCleanWhite;
            }
            else if (themeName.Equals(ThemeRoyalBlue, StringComparison.OrdinalIgnoreCase) ||
                     themeName.Contains("royal", StringComparison.OrdinalIgnoreCase) ||
                     themeName.Contains("blue", StringComparison.OrdinalIgnoreCase))
            {
                ApplyRoyalBlueTheme();
                CurrentTheme = ThemeRoyalBlue;
            }
            else
            {
                // Night / BiotechDark / unknown → dark
                ApplyBiotechDarkTheme();
                CurrentTheme = ThemeBiotechDark;
            }

            ThemeChanged?.Invoke(CurrentTheme);
        }

        // ══════════════════════════════════════════════════════════════════
        //  THEME 1: Medical Clean White
        //  Background: Pure white/off-white  |  Accent: #0070C0 (medical blue)
        //  Text on white: near-black #0D1B2A  |  Zero white-on-white
        // ══════════════════════════════════════════════════════════════════
        private static void ApplyCleanWhiteTheme()
        {
            SetBrush("AppBackgroundBrush",              "#F0F4F8");  // cool off-white
            SetBrush("SidebarBrush",                    "#FFFFFF");
            SetBrush("CardBrush",                       "#FFFFFF");
            SetBrush("CardHoverBrush",                  "#E8F0F8");  // subtle blue tint
            SetBrush("InputBrush",                      "#F7FAFC");
            SetBrush("BorderBrush",                     "#B8C8D8");
            SetBrush("BorderFocusBrush",                "#0070C0");

            // Foreground – rich near-black for maximum legibility on white
            SetBrush("TextPrimaryBrush",                "#0D1B2A");
            SetBrush("TextSecondaryBrush",              "#3A5068");
            SetBrush("TextMutedBrush",                  "#607D8B");

            // Brand & action colours
            SetBrush("PrimaryBrush",                    "#0070C0");  // WHO / hospital blue
            SetBrush("PrimaryHoverBrush",               "#005A9E");
            SetBrush("AccentBrush",                     "#0070C0");
            SetBrush("SuccessBrush",                    "#027A48");  // deep green
            SetBrush("SuccessHoverBrush",               "#015E38");
            SetBrush("DangerBrush",                     "#C5281C");
            SetBrush("DangerHoverBrush",                "#A01F14");
            SetBrush("WarningBrush",                    "#B45309");

            // Top Bar – authoritative clinical blue header to break all-white monotony
            SetBrush("TopBarBrush",                     "#0A3D62");  // Deep Hospital Medical Blue
            SetBrush("TopBarBorderBrush",               "#082F4D");
            SetBrush("TopBarCardBrush",                 "#0E4B77");
            SetBrush("TopBarTextPrimaryBrush",          "#FFFFFF");
            SetBrush("TopBarTextSecondaryBrush",        "#BAE6FD");

            // DataGrid – alternating pale rows, dark header text
            SetBrush("DataGridRowBackgroundBrush",      "#FFFFFF");
            SetBrush("DataGridAltRowBackgroundBrush",   "#EEF4FB");
            SetBrush("DataGridHeaderBackgroundBrush",   "#D6E6F4");  // blue-tinted header
            SetBrush("DataGridGridLinesBrush",          "#C5D8EA");
        }

        // ══════════════════════════════════════════════════════════════════
        //  THEME 2: Biotech Dark Mode
        //  Background: deep slate #0B0F1A  |  Accent: #38BDF8 (sky blue)
        //  Text on dark: white #F1F5F9  |  Zero dark-on-dark
        // ══════════════════════════════════════════════════════════════════
        private static void ApplyBiotechDarkTheme()
        {
            SetBrush("AppBackgroundBrush",              "#0B0F1A");
            SetBrush("SidebarBrush",                    "#0F172A");
            SetBrush("CardBrush",                       "#1A2540");
            SetBrush("CardHoverBrush",                  "#213057");
            SetBrush("InputBrush",                      "#0F1B30");
            SetBrush("BorderBrush",                     "#2D4060");
            SetBrush("BorderFocusBrush",                "#38BDF8");

            // Top Bar
            SetBrush("TopBarBrush",                     "#0F172A");
            SetBrush("TopBarBorderBrush",               "#2D4060");
            SetBrush("TopBarCardBrush",                 "#1A2540");
            SetBrush("TopBarTextPrimaryBrush",          "#F1F5F9");
            SetBrush("TopBarTextSecondaryBrush",        "#94B4D4");

            // Foreground – bright for contrast on very dark surfaces
            SetBrush("TextPrimaryBrush",                "#F1F5F9");
            SetBrush("TextSecondaryBrush",              "#94B4D4");
            SetBrush("TextMutedBrush",                  "#5A7A9A");

            SetBrush("PrimaryBrush",                    "#0284C7");
            SetBrush("PrimaryHoverBrush",               "#0369A1");
            SetBrush("AccentBrush",                     "#38BDF8");
            SetBrush("SuccessBrush",                    "#10B981");
            SetBrush("SuccessHoverBrush",               "#059669");
            SetBrush("DangerBrush",                     "#EF4444");
            SetBrush("DangerHoverBrush",                "#DC2626");
            SetBrush("WarningBrush",                    "#F59E0B");

            SetBrush("DataGridRowBackgroundBrush",      "#1A2540");
            SetBrush("DataGridAltRowBackgroundBrush",   "#131D35");
            SetBrush("DataGridHeaderBackgroundBrush",   "#0F172A");
            SetBrush("DataGridGridLinesBrush",          "#2D4060");
        }

        // ══════════════════════════════════════════════════════════════════
        //  THEME 3: Clinical Royal Blue
        //  Background: #0A1F3D (deep navy)  |  Accent: #4FC3F7 (crystal blue)
        //  Sidebar:    #112249              |  Text on navy: pure white
        // ══════════════════════════════════════════════════════════════════
        private static void ApplyRoyalBlueTheme()
        {
            SetBrush("AppBackgroundBrush",              "#071629");  // near-black navy
            SetBrush("SidebarBrush",                    "#0A2040");
            SetBrush("CardBrush",                       "#0F2D55");
            SetBrush("CardHoverBrush",                  "#163568");
            SetBrush("InputBrush",                      "#0C2448");
            SetBrush("BorderBrush",                     "#1E4A80");
            SetBrush("BorderFocusBrush",                "#4FC3F7");

            // Top Bar
            SetBrush("TopBarBrush",                     "#0A2040");
            SetBrush("TopBarBorderBrush",               "#1E4A80");
            SetBrush("TopBarCardBrush",                 "#0F2D55");
            SetBrush("TopBarTextPrimaryBrush",          "#FFFFFF");
            SetBrush("TopBarTextSecondaryBrush",        "#90C8F0");

            // White / near-white text on deep navy – max legibility
            SetBrush("TextPrimaryBrush",                "#FFFFFF");
            SetBrush("TextSecondaryBrush",              "#90C8F0");
            SetBrush("TextMutedBrush",                  "#4A84B8");

            SetBrush("PrimaryBrush",                    "#1565C0");  // material blue 800
            SetBrush("PrimaryHoverBrush",               "#0D47A1");
            SetBrush("AccentBrush",                     "#4FC3F7");  // crystal sky
            SetBrush("SuccessBrush",                    "#00C853");  // vivid green on navy
            SetBrush("SuccessHoverBrush",               "#00A846");
            SetBrush("DangerBrush",                     "#FF5252");
            SetBrush("DangerHoverBrush",                "#FF1744");
            SetBrush("WarningBrush",                    "#FFD740");

            SetBrush("DataGridRowBackgroundBrush",      "#0F2D55");
            SetBrush("DataGridAltRowBackgroundBrush",   "#0C2448");
            SetBrush("DataGridHeaderBackgroundBrush",   "#0A2040");
            SetBrush("DataGridGridLinesBrush",          "#1E4A80");
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private static void SetBrush(string key, string hexColor)
        {
            var color = ColorFromHex(hexColor);
            var app   = Application.Current;
            if (app == null) return;

            if (app.Resources.Contains(key) &&
                app.Resources[key] is SolidColorBrush brush &&
                !brush.IsFrozen)
            {
                brush.Color = color;
            }
            else
            {
                app.Resources[key] = new SolidColorBrush(color);
            }
        }

        public static Color ColorFromHex(string hex) =>
            (Color)ColorConverter.ConvertFromString(hex);
    }
}
