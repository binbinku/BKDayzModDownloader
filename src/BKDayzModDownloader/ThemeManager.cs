using System.Windows;
using System.Windows.Media;

namespace BKDayzModDownloader;

internal static class ThemeManager
{
    public static string Current { get; private set; } = "Light";
    public static void Apply(string? theme)
    {
        var dark = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase);
        Current = dark ? "Dark" : "Light";
        var values = new Dictionary<string, string>
        {
            ["Surface"] = dark ? "#0A0A0A" : "#FFFFFF",
            ["Subtle"] = dark ? "#111111" : "#FAFAFA",
            ["Ink"] = dark ? "#EDEDED" : "#171717",
            ["Muted"] = dark ? "#A1A1A1" : "#737373",
            ["Faint"] = dark ? "#737373" : "#A3A3A3",
            ["Accent"] = dark ? "#EDEDED" : "#171717",
            ["AccentInk"] = dark ? "#0A0A0A" : "#FFFFFF",
            ["Line"] = dark ? "#2E2E2E" : "#EAEAEA",
            ["LineStrong"] = dark ? "#525252" : "#D4D4D4",
            ["Hover"] = dark ? "#262626" : "#F5F5F5",
            ["AccentHover"] = dark ? "#D4D4D4" : "#404040",
            ["Track"] = dark ? "#333333" : "#EDEDED",
            ["Success"] = dark ? "#6FE3AA" : "#176C57",
            ["Danger"] = dark ? "#FF8A8A" : "#C13D38"
        };
        foreach (var (key, value) in values)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            Application.Current.Resources[key] = brush;
        }
    }
}
