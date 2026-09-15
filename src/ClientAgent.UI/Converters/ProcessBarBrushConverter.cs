using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ClientAgent.UI.Enums;

namespace ClientAgent.UI.Converters;

public sealed class ProcessBarBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = values.Length > 0 ? ToDouble(values[0]) : 0d;
        var sortBy = values.Length > 1 && values[1] is ProcessSortBy parsed
            ? parsed
            : ProcessSortBy.Cpu;

        return sortBy switch
        {
            ProcessSortBy.Ram => Brush("ProcessBarRamBrush", Color.FromRgb(0x21, 0x96, 0xF3)),
            ProcessSortBy.Network => Brush("ProcessBarNetworkBrush", Color.FromRgb(0x9C, 0x27, 0xB0)),
            _ when percent >= 50 => Brush("ProcessBarCpuHighBrush", Color.FromRgb(0xF4, 0x43, 0x36)),
            _ when percent >= 20 => Brush("ProcessBarCpuMidBrush", Color.FromRgb(0xFF, 0xC1, 0x07)),
            _ => Brush("ProcessBarCpuLowBrush", Color.FromRgb(0x4C, 0xAF, 0x50))
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static double ToDouble(object? value) => value switch
    {
        double d => d,
        float f => f,
        int i => i,
        _ => 0d
    };

    private static Brush Brush(string key, Color fallback)
    {
        if (Application.Current?.TryFindResource(key) is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }
}
