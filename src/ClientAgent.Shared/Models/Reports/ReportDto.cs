namespace ClientAgent.Shared.Models.Reports;

/// <summary>A report built by the service: a list of sections, each with key figures, charts and / or a table.</summary>
public sealed class ReportDto
{
    public string Type { get; set; } = string.Empty;

    /// <summary>For the details report: what it is about, e.g. "cpu", "disk:C:" or "point:web1".</summary>
    public string? Subject { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string MachineName { get; set; } = string.Empty;

    /// <summary>Local time.</summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>Local time.</summary>
    public DateTime From { get; set; }

    /// <summary>Local time.</summary>
    public DateTime To { get; set; }

    public List<ReportSection> Sections { get; set; } = [];
}

public sealed class ReportSection
{
    public string Title { get; set; } = string.Empty;

    public string? Note { get; set; }

    public List<ReportMetric> Metrics { get; set; } = [];

    public List<ReportChart> Charts { get; set; } = [];

    public List<string> Columns { get; set; } = [];

    public List<List<string>> Rows { get; set; } = [];

    /// <summary>Same length as <see cref="Rows"/> when set: the details subject each row opens, or null.</summary>
    public List<string?>? RowLinks { get; set; }
}

/// <param name="Status">"Good", "Warning", "Bad" or null, for the color of the value.</param>
/// <param name="Change">Comparison with the previous period of the same length, e.g. "+12% vs previous period".</param>
/// <param name="ChangeStatus">"Good", "Warning", "Bad" or null, for the color of the comparison.</param>
public sealed record ReportMetric(string Label, string Value, string? Status = null, string? Change = null, string? ChangeStatus = null);

public static class ReportChartKinds
{
    /// <summary>One line per series over time; <see cref="ReportChart.Labels"/> are the times.</summary>
    public const string Line = "Line";

    /// <summary>Bars per label (hours, days, names); one bar per series in each group.</summary>
    public const string Bar = "Bar";

    /// <summary>Slices of the first series; <see cref="ReportChart.Labels"/> are the slice names.</summary>
    public const string Pie = "Pie";

    /// <summary>One colored band per series over time. Values: 2 working, 1 warning, 0 down, -1 no reading.</summary>
    public const string Timeline = "Timeline";

    /// <summary>A grid: one row per series, one column per label; the color shows the value (-1 = no reading).</summary>
    public const string Heatmap = "Heatmap";
}

public sealed class ReportChart
{
    public string Kind { get; set; } = ReportChartKinds.Line;

    public string Title { get; set; } = string.Empty;

    /// <summary>Draw at half the page width (two charts side by side).</summary>
    public bool Half { get; set; }

    public string Unit { get; set; } = string.Empty;

    /// <summary>Fixed top of the scale; 0 scales to the data.</summary>
    public double Maximum { get; set; }

    public List<string> Labels { get; set; } = [];

    public List<ReportSeries> Series { get; set; } = [];

    /// <summary>Horizontal reference lines on line and bar charts, such as the warning and problem levels.</summary>
    public List<ReportThreshold> Thresholds { get; set; } = [];
}

public sealed record ReportSeries(string Name, List<double> Values);

/// <param name="Status">"Warning" or "Bad", for the color of the line.</param>
public sealed record ReportThreshold(string Name, double Value, string Status);
