using System.Windows;
using System.Windows.Media;

namespace ClientAgent.UI.Services;

public static class UiTheme
{
    public static void Apply(string? theme)
    {
        var light = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
        SetBrush("BackgroundDarkBrush", light ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x1E, 0x1E, 0x1E));
        SetBrush("SurfaceDarkBrush", light ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x25, 0x25, 0x26));
        SetBrush("SurfaceDarkAltBrush", light ? Color.FromRgb(0xEE, 0xEE, 0xF2) : Color.FromRgb(0x2D, 0x2D, 0x30));
        SetBrush("BorderColorBrush", light ? Color.FromRgb(0xD0, 0xD0, 0xD4) : Color.FromRgb(0x3E, 0x3E, 0x42));
        SetBrush("TextPrimaryBrush", light ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White);
        SetBrush("TextSecondaryBrush", light ? Color.FromRgb(0x5C, 0x5C, 0x66) : Color.FromRgb(0xAA, 0xAA, 0xAA));
    }

    private static void SetBrush(string key, Color color)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        if (resources[key] is SolidColorBrush existing && !existing.IsFrozen)
        {
            existing.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }
}
