using System.Windows;
using System.Windows.Media;

namespace MonitorAgent.UI.Services;

public static class UiTheme
{
    public static event EventHandler? Changed;

    public static void Apply(string? theme)
    {
        var light = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
        SetBrush("BackgroundDarkBrush", light ? Rgb(0xEB, 0xEB, 0xEB) : Rgb(0x16, 0x16, 0x16));
        SetBrush("SurfaceDarkBrush", light ? Rgb(0xF7, 0xF7, 0xF7) : Rgb(0x1C, 0x1C, 0x1D));
        SetBrush("SurfaceDarkAltBrush", light ? Rgb(0xE6, 0xE6, 0xE6) : Rgb(0x24, 0x24, 0x27));
        SetBrush("BorderColorBrush", light ? Rgb(0xCC, 0xCC, 0xCC) : Rgb(0x34, 0x34, 0x38));
        SetBrush("TextPrimaryBrush", light ? Rgb(0x1A, 0x1A, 0x1A) : Colors.White);
        SetBrush("TextSecondaryBrush", light ? Rgb(0x44, 0x44, 0x4E) : Rgb(0xAA, 0xAA, 0xAA));
        SetBrush("InputBrush", light ? Rgb(0xFC, 0xFC, 0xFC) : Rgb(0x13, 0x13, 0x18));
        SetBrush("HoverBrush", light ? Rgb(0xDE, 0xDE, 0xDE) : Rgb(0x26, 0x26, 0x29));
        SetBrush("SelectedBrush", light ? Rgb(0xDA, 0xEB, 0xDA) : Rgb(0x2A, 0x2A, 0x2F));
        SetBrush("SelectedAccentBrush", light ? Rgb(0xB8, 0xDB, 0xBB) : Rgb(0x27, 0x40, 0x2F));
        SetBrush("ButtonBrush", light ? Rgb(0xDC, 0xDC, 0xDC) : Rgb(0x32, 0x32, 0x32));
        SetBrush("ButtonTextBrush", light ? Rgb(0x1A, 0x1A, 0x1A) : Colors.White);
        SetBrush("AltRowBrush", light ? Rgb(0xF0, 0xF0, 0xF0) : Rgb(0x1A, 0x1A, 0x1A));
        SetBrush("DividerBrush", light ? Rgb(0xCC, 0xCC, 0xCC) : Rgb(0x31, 0x31, 0x43));
        SetBrush("ChipFillBrush", light ? Rgb(0xF7, 0xF7, 0xF7) : Rgb(0x13, 0x13, 0x18));
        SetBrush("NavHoverBrush", light ? Rgb(0xD4, 0xD4, 0xD4) : Rgb(0x3A, 0x3A, 0x40));
        SetBrush("ScrollThumbBrush", light ? Rgb(0xD4, 0xD4, 0xD4) : Rgb(0x3A, 0x3A, 0x40));
        SetBrush("ScrollThumbHoverBrush", light ? Rgb(0xC6, 0xC6, 0xC6) : Rgb(0x46, 0x46, 0x4C));
        SetBrush("ScrollThumbDragBrush", light ? Rgb(0xBA, 0xBA, 0xBA) : Rgb(0x52, 0x52, 0x58));
        SetBrush("IssueBoardBrush", light ? Rgb(0xFA, 0xF0, 0xDE) : Rgb(0x1C, 0x15, 0x0D));
        SetBrush("IssueBoardBorderBrush", light ? Rgb(0xD9, 0xB8, 0x76) : Rgb(0x4E, 0x33, 0x12));
        SetBrush("IssueTextBrush", light ? Rgb(0x3A, 0x2A, 0x12) : Rgb(0xF0, 0xF0, 0xF0));
        SetBrush("ToolTipBrush", light ? Colors.White : Rgb(0x2B, 0x2B, 0x2F));
        SetBrush("ToolTipBorderBrush", light ? Rgb(0xC8, 0xC8, 0xC8) : Rgb(0x48, 0x48, 0x4E));
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

    /// <summary>A theme brush for view models, which do not know the UI framework's brush type.</summary>
    public static object Resource(string key, string fallbackHex)
        => Application.Current?.TryFindResource(key) as Brush ?? FromHex(fallbackHex);

    public static object FromHex(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
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
