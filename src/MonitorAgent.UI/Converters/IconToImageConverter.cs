using System.Globalization;
using System.Windows.Data;
using MonitorAgent.UI.Services;

namespace MonitorAgent.UI.Converters;

public sealed class IconToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string icon ? SiteLogoCache.ToImage(icon) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
