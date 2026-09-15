using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ClientAgent.UI.Converters;

public sealed class HealthPercentToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            _ => 100d
        };

        if (percent >= 80) return Application.Current.FindResource("AccentGreenBrush");
        if (percent >= 50) return Application.Current.FindResource("AccentYellowBrush");
        return Application.Current.FindResource("AccentRedBrush");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
