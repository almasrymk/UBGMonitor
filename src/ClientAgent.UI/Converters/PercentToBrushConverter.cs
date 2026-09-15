using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ClientAgent.UI.Converters;

public sealed class PercentToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = ToDouble(value);
        if (percent >= 90) return Brush("AccentRed");
        if (percent >= 75) return Brush("AccentYellow");
        return Brush("AccentGreen");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static double ToDouble(object? value) => value switch
    {
        double d => d,
        float f => f,
        int i => i,
        _ => 0d
    };

    private static Brush Brush(string key)
        => (Brush)Application.Current.FindResource(key + "Brush");
}
