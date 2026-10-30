using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

using AutoCommand.Agent.Core;
using AutoCommand.Contain.Core;
using AutoCommand.Forensics.Core;
using AutoCommand.NetWatch.Core;
using AutoCommand.Sitedoctor.Core;

namespace AutoCommand.Services
{
    /// <summary>
    /// Dark / Light theme switching. Replaces application-level brush resources
    /// at runtime — every theme-brush reference in XAML is a DynamicResource, so
    /// the swap applies live without a restart. Choice persists in HKCU.
    ///
    /// Two identities: Dark  = the operator console (deep slate, blues).
    /// Light = the Shields Up look — Apple-style light theme from the Shields Up
    /// app's design system (#ECF2F9 canvas, white surfaces, #EBEBEB hairlines,
    /// #1D1D1F text, #007AFF accent, status colors tuned ≥4.5:1 on light).
    /// (Some brush KEY names retain historical "Violet/Pink/Cyan" wording —
    /// values are all blues now; keys are internal identifiers referenced
    /// across the XAML.)
    /// </summary>
    public static class ThemeService
    {
        private const string RegKey = @"Software\AutoCommand";
        private const string RegName = "Theme";

        // (resource key, dark value, light value — light = Shields Up palette)
        private static readonly (string Key, string Dark, string Light)[] BrushMap =
        {
            ("BackgroundBrush",    "#171B22", "#ECF2F9"),
            ("SurfaceBrush",       "#1F242E", "#FFFFFF"),
            ("SurfaceAltBrush",    "#282F3B", "#F6F8FA"),
            ("BorderBrush",        "#39414F", "#EBEBEB"),
            ("TextPrimaryBrush",   "#F1F4F9", "#1D1D1F"),
            ("TextSecondaryBrush", "#98A2B3", "#86868B"),
            ("AccentBlueBrush",    "#3B82F6", "#007AFF"),
            ("AccentGreenBrush",   "#34D399", "#1E8E3E"),
            ("AccentAmberBrush",   "#F59E0B", "#B26A00"),
            ("AccentRedBrush",     "#F87171", "#D70015"),
            ("AccentVioletBrush",  "#4A9EEF", "#0062CC"),   // hover accent — blue now
            ("AccentPinkBrush",    "#5FA8F5", "#3E86D6"),   // secondary accent — blue now
            ("AccentCyanBrush",    "#53B4E8", "#0051A8"),
            ("NavSelectedBrush",   "#16202E", "#E5F1FF"),
            ("NavHoverBrush",      "#101823", "#ECF2F9"),
            ("RowAltBrush",        "#20262F", "#F6F8FA"),
            ("RowHoverBrush",      "#262E3A", "#EBF0F6"),
            ("RowSelectedBrush",   "#31405A", "#E5F1FF"),
            ("GridLineBrush",      "#2A323E", "#EBEBEB"),
            // pill/badge backgrounds — dark uses the old hand-tuned tints,
            // light uses Shields Up's accent-at-15% washes
            ("BadgeRedBg",         "#3A1520", "#26D70015"),
            ("BadgeAmberBg",       "#3A2A14", "#26B26A00"),
            ("BadgeBlueBg",        "#1A4A8A", "#26007AFF"),
            // between TextPrimary and TextSecondary for 10-11 px captions
            ("MutedTextBrush",     "#B9C3D2", "#6E7681"),
        };

        public static string Saved =>
            Registry.GetValue($@"HKEY_CURRENT_USER\{RegKey}", RegName, "Dark") as string ?? "Dark";

        /// <summary>Apply a theme now and remember it. theme: "Dark" | "Light".</summary>
        public static void ApplyTheme(string theme)
        {
            bool light = theme == "Light";
            var res = Application.Current.Resources;
            foreach (var (key, dark, lightVal) in BrushMap)
                res[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? lightVal : dark));

            if (light)
            {
                res["PrimaryGradientBrush"] = MakeGradient("#007AFF", "#0062CC");
                res["DangerGradientBrush"] = MakeGradient("#D70015", "#B40011");
                res["TabIndicatorGradientBrush"] = MakeGradient("#007AFF", "#3E86D6");
            }
            else
            {
                res["PrimaryGradientBrush"] = MakeGradient("#3B82F6", "#60A5FA");
                res["DangerGradientBrush"] = MakeGradient("#EF4444", "#F87171");
                res["TabIndicatorGradientBrush"] = MakeGradient("#60A5FA", "#3B82F6");
            }

            using var k = Registry.CurrentUser.CreateSubKey(RegKey);
            k.SetValue(RegName, light ? "Light" : "Dark");
        }

        private static LinearGradientBrush MakeGradient(params string[] stops)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            double step = stops.Length > 1 ? 1.0 / (stops.Length - 1) : 0;
            for (int i = 0; i < stops.Length; i++)
                brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(stops[i]), i * step));
            return brush;
        }
    }
}
