using System.Globalization;
using Avalonia.Media;

namespace MonitorAgent.Desktop.Converters;

public sealed class MonitorPointBadgeConverter : OneWayConverter
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? "Unknown";
        if (parameter?.ToString() == "Type")
            return Brush.Parse(text switch
            {
                "Device" => "#44266C",
                "Website" => "#214A72",
                "Database" => "#235B60",
                "Application" => "#594129",
                _ => "#303A40"
            });
        var state = text.Contains("Critical", StringComparison.OrdinalIgnoreCase) || text.Contains("Problem", StringComparison.OrdinalIgnoreCase) ? "Problem"
            : text.Contains("Warning", StringComparison.OrdinalIgnoreCase) ? "Warning"
            : text.Contains("Healthy", StringComparison.OrdinalIgnoreCase) || text.Equals("OK", StringComparison.OrdinalIgnoreCase) ? "Healthy" : "Unknown";
        if (parameter?.ToString() == "Label") return state;
        return Brush.Parse((parameter?.ToString() == "Result", state) switch
        {
            (true, "Healthy") => "#35DA65",
            (true, "Warning") => "#FFBF00",
            (true, "Problem") => "#FF4040",
            (true, _) => "#9E9E9E",
            (false, "Healthy") => "#175530",
            (false, "Warning") => "#BB8500",
            (false, "Problem") => "#B5232C",
            _ => "#454B50"
        });
    }
}
