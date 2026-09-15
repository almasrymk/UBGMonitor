using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ClientAgent.UI.Converters;

public sealed class BoolToStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? Application.Current.FindResource("AccentGreenBrush")
            : Application.Current.FindResource("AccentRedBrush");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
