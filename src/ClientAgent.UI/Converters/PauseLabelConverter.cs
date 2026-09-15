using System.Globalization;
using System.Windows.Data;

namespace ClientAgent.UI.Converters;

public sealed class PauseLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Resume" : "Pause";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
