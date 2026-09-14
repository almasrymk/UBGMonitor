using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ClientAgent.UI.Converters;

public sealed class PercentToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            _ => 0d
        };

        if (percent >= 90)
        {
            return new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
        }

        if (percent >= 75)
        {
            return new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
        }

        return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
