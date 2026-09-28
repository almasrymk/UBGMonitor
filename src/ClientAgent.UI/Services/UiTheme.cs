using System.Windows;
using System.Windows.Media;

namespace ClientAgent.UI.Services;

public static class UiTheme
{
    public static event EventHandler? Changed;

    public static void Apply(string? theme)
    {
        var light = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
        SetBrush("BackgroundDarkBrush", light ? Rgb(0xF3, 0xF3, 0xF3) : Rgb(0x1E, 0x1E, 0x1E));
        SetBrush("SurfaceDarkBrush", light ? Colors.White : Rgb(0x25, 0x25, 0x26));
        SetBrush("SurfaceDarkAltBrush", light ? Rgb(0xEE, 0xEE, 0xF2) : Rgb(0x2D, 0x2D, 0x30));
        SetBrush("BorderColorBrush", light ? Rgb(0xD0, 0xD0, 0xD4) : Rgb(0x3E, 0x3E, 0x42));
        SetBrush("TextPrimaryBrush", light ? Rgb(0x1A, 0x1A, 0x1A) : Colors.White);
        SetBrush("TextSecondaryBrush", light ? Rgb(0x5C, 0x5C, 0x66) : Rgb(0xAA, 0xAA, 0xAA));
        SetBrush("InputBrush", light ? Colors.White : Rgb(0x1B, 0x1B, 0x20));
        SetBrush("HoverBrush", light ? Rgb(0xE6, 0xE6, 0xEC) : Rgb(0x2E, 0x2E, 0x32));
        SetBrush("SelectedBrush", light ? Rgb(0xE7, 0xF2, 0xEA) : Rgb(0x33, 0x33, 0x38));
        SetBrush("SelectedAccentBrush", light ? Rgb(0xC8, 0xE6, 0xC9) : Rgb(0x2F, 0x4A, 0x38));
        SetBrush("ButtonBrush", light ? Rgb(0xE4, 0xE4, 0xEA) : Rgb(0x3C, 0x3C, 0x3C));
        SetBrush("ButtonTextBrush", light ? Rgb(0x1A, 0x1A, 0x1A) : Colors.White);
        SetBrush("AltRowBrush", light ? Rgb(0xF7, 0xF7, 0xF9) : Rgb(0x22, 0x22, 0x22));
        SetBrush("DividerBrush", light ? Rgb(0xD0, 0xD0, 0xD4) : Rgb(0x3A, 0x3A, 0x4D));
        SetBrush("ChipFillBrush", light ? Colors.White : Rgb(0x1B, 0x1B, 0x20));
        SetBrush("NavHoverBrush", light ? Rgb(0xD8, 0xD8, 0xDE) : Rgb(0x44, 0x44, 0x4A));
        SetBrush("IssueBoardBrush", light ? Rgb(0xFF, 0xF6, 0xE8) : Rgb(0x24, 0x1C, 0x12));
        SetBrush("IssueBoardBorderBrush", light ? Rgb(0xE6, 0xC9, 0x8A) : Rgb(0x5A, 0x3B, 0x16));
        SetBrush("IssueTextBrush", light ? Rgb(0x3A, 0x2A, 0x12) : Rgb(0xF0, 0xF0, 0xF0));
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static Brush Brush(string key, Color fallback)
    {
        if (Application.Current?.TryFindResource(key) is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

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
