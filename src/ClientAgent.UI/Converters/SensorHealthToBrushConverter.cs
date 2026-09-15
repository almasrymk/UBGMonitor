using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ClientAgent.UI.Enums;

namespace ClientAgent.UI.Converters;

public sealed class SensorHealthToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Ok = Freeze(0x4C, 0xAF, 0x50);
    private static readonly SolidColorBrush Warning = Freeze(0xFF, 0xC1, 0x07);
    private static readonly SolidColorBrush Critical = Freeze(0xF4, 0x43, 0x36);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SensorHealth.Ok ? Ok
            : value is SensorHealth.Warning ? Warning
            : Critical;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
