using System.Globalization;

namespace ClientAgent.Shared.Reports;

/// <summary>Colors and number formats shared by the printed report and the charts in the app.</summary>
public static class ReportPalette
{
    public static readonly string[] Series = ["#2e7d32", "#1565c0", "#ef6c00", "#6a1b9a", "#00838f", "#c62828", "#6d4c41", "#546e7a"];

    public const string Good = "#2e7d32";
    public const string Warning = "#f9a825";
    public const string Bad = "#c62828";
    public const string NoReading = "#e0e0e0";

    public static string SeriesColor(int index) => Series[index % Series.Length];

    /// <summary>"Good", "Warning" or "Bad" to a color; anything else is gray.</summary>
    public static string Status(string? status) => status switch
    {
        "Good" => Good,
        "Warning" => Warning,
        "Bad" => Bad,
        _ => "#757575"
    };

    /// <summary>Timeline value: 2 working, 1 warning, 0 down, below zero no reading.</summary>
    public static string State(double value) => value switch
    {
        >= 2 => Good,
        >= 1 => Warning,
        >= 0 => Bad,
        _ => NoReading
    };

    public static string StateName(double value) => value switch
    {
        >= 2 => "Working",
        >= 1 => "Warning",
        >= 0 => "Down",
        _ => "No reading"
    };

    /// <summary>Green for low, yellow for the middle, red for the top of the scale; gray for no reading.</summary>
    public static string Heat(double value, double maximum)
    {
        if (value < 0)
        {
            return NoReading;
        }

        var t = maximum <= 0 ? 0 : Math.Clamp(value / maximum, 0, 1);
        (int R, int G, int B) low = (0x43, 0xa0, 0x47), middle = (0xfd, 0xd8, 0x35), high = (0xe5, 0x39, 0x35);
        var (from, to, f) = t < 0.5 ? (low, middle, t * 2) : (middle, high, (t - 0.5) * 2);
        return $"#{Mix(from.R, to.R, f):x2}{Mix(from.G, to.G, f):x2}{Mix(from.B, to.B, f):x2}";
    }

    /// <summary>A round top for a chart scale: 1, 2 or 5 times a power of ten.</summary>
    public static double NiceMaximum(IEnumerable<double> values)
    {
        var peak = values.DefaultIfEmpty(0).Max();
        if (peak <= 0)
        {
            return 1;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(peak)));
        foreach (var factor in new[] { 1d, 2d, 5d, 10d })
        {
            if (peak <= factor * magnitude)
            {
                return factor * magnitude;
            }
        }

        return 10 * magnitude;
    }

    public static string Number(double value) =>
        value.ToString(Math.Abs(value) >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture);

    private static int Mix(int from, int to, double f) => (int)Math.Round(from + (to - from) * f);
}
