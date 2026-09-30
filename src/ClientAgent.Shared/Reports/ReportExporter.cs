using System.Globalization;
using System.Net;
using System.Text;
using ClientAgent.Shared.Models.Reports;

namespace ClientAgent.Shared.Reports;

/// <summary>Turns a report into a printable HTML page (print it to PDF from the browser) or a CSV file for Excel.</summary>
public static class ReportExporter
{
    public const string ProgramName = "Agent Monitor";

    private static readonly Lazy<string?> Logo = new(LoadLogo);

    public static string ToHtml(ReportDto report)
    {
        var html = new StringBuilder();
        var generated = report.GeneratedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        html.Append($"<title>{E(report.Title)} - {E(report.MachineName)} - {report.From:yyyy-MM-dd}</title><style>");
        html.Append(Style);
        html.Append("@page{size:A4;margin:16mm 12mm 18mm 12mm;");
        html.Append($"@top-left{{content:\"{Css(ProgramName)} - {Css(report.Title)}\";font:9px 'Segoe UI',Arial;color:#777}}");
        html.Append($"@top-right{{content:\"{Css(report.MachineName)}\";font:9px 'Segoe UI',Arial;color:#777}}");
        html.Append($"@bottom-left{{content:\"Generated {Css(generated)}\";font:9px 'Segoe UI',Arial;color:#777}}");
        html.Append("@bottom-center{content:\"Page \" counter(page) \" of \" counter(pages);font:9px 'Segoe UI',Arial;color:#777}");
        html.Append($"@bottom-right{{content:\"{Css(report.From.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))} - {Css(report.To.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))}\";font:9px 'Segoe UI',Arial;color:#777}}}}");
        html.Append("</style></head><body>");
        html.Append("<button class=\"print\" onclick=\"window.print()\">Print / Save as PDF</button>");

        html.Append("<header class=\"cover\">");
        if (Logo.Value is { } logo)
        {
            html.Append($"<img class=\"logo\" src=\"data:image/png;base64,{logo}\" alt=\"\">");
        }

        html.Append($"<div class=\"heading\"><div class=\"program\">{E(ProgramName)}</div><h1>{E(report.Title)}</h1>");
        html.Append($"<div class=\"desc\">{E(report.Description)}</div></div></header>");
        html.Append("<table class=\"facts\"><tr>");
        html.Append($"<td><span>Device</span>{E(report.MachineName)}</td>");
        html.Append($"<td><span>From</span>{report.From:yyyy-MM-dd HH:mm}</td>");
        html.Append($"<td><span>To</span>{report.To:yyyy-MM-dd HH:mm}</td>");
        html.Append($"<td><span>Length</span>{E(Length(report.To - report.From))}</td>");
        html.Append($"<td><span>Generated</span>{E(generated)}</td>");
        html.Append("</tr></table>");

        if (report.Sections.Count > 3)
        {
            html.Append("<nav class=\"toc\"><b>Contents</b><ol>");
            for (var i = 0; i < report.Sections.Count; i++)
            {
                html.Append($"<li><a href=\"#s{i}\">{E(report.Sections[i].Title)}</a></li>");
            }

            html.Append("</ol></nav>");
        }

        for (var i = 0; i < report.Sections.Count; i++)
        {
            AppendSection(html, report.Sections[i], i);
        }

        html.Append($"<footer class=\"end\">{E(ProgramName)} &middot; {E(report.MachineName)} &middot; Generated {E(generated)}</footer>");
        html.Append("</body></html>");
        return html.ToString();
    }

    /// <summary>Every table and chart of the report, one after the other, each under its section title (opens in Excel).</summary>
    public static string ToCsv(ReportDto report)
    {
        var csv = new StringBuilder();
        csv.AppendLine(Row([report.Title]));
        csv.AppendLine(Row(["Device", report.MachineName]));
        csv.AppendLine(Row(["Period", $"{report.From:yyyy-MM-dd HH:mm}", $"{report.To:yyyy-MM-dd HH:mm}"]));
        csv.AppendLine(Row(["Generated", $"{report.GeneratedAt:yyyy-MM-dd HH:mm}"]));
        foreach (var section in report.Sections)
        {
            csv.AppendLine();
            csv.AppendLine(Row([section.Title]));
            foreach (var metric in section.Metrics)
            {
                csv.AppendLine(Row(metric.Change is null ? [metric.Label, metric.Value] : [metric.Label, metric.Value, metric.Change]));
            }

            if (section.Columns.Count > 0)
            {
                csv.AppendLine(Row(section.Columns));
                foreach (var row in section.Rows)
                {
                    csv.AppendLine(Row(row));
                }
            }

            foreach (var chart in section.Charts.Where(chart => chart.Labels.Count > 0 && chart.Series.Count > 0))
            {
                csv.AppendLine();
                csv.AppendLine(Row([chart.Title]));
                if (chart.Kind is ReportChartKinds.Timeline or ReportChartKinds.Heatmap)
                {
                    csv.AppendLine(Row(["", .. chart.Labels]));
                    foreach (var series in chart.Series)
                    {
                        csv.AppendLine(Row([series.Name, .. series.Values.Select(value => chart.Kind == ReportChartKinds.Timeline
                            ? ReportPalette.StateName(value)
                            : value < 0 ? string.Empty : Invariant(value))]));
                    }

                    continue;
                }

                var unit = string.IsNullOrEmpty(chart.Unit) ? string.Empty : $" ({chart.Unit})";
                csv.AppendLine(Row(["", .. chart.Series.Select(series => series.Name + unit)]));
                for (var i = 0; i < chart.Labels.Count; i++)
                {
                    csv.AppendLine(Row([chart.Labels[i], .. chart.Series.Select(series => i < series.Values.Count ? Invariant(series.Values[i]) : string.Empty)]));
                }
            }
        }

        return csv.ToString();
    }

    private static void AppendSection(StringBuilder html, ReportSection section, int index)
    {
        html.Append($"<section id=\"s{index}\"><h2>{E(section.Title)}</h2>");
        if (!string.IsNullOrWhiteSpace(section.Note))
        {
            html.Append($"<div class=\"note\">{E(section.Note)}</div>");
        }

        if (section.Metrics.Count > 0)
        {
            html.Append("<div class=\"metrics\">");
            foreach (var metric in section.Metrics)
            {
                html.Append($"<div class=\"metric {E(metric.Status ?? string.Empty)}\"><div class=\"label\">{E(metric.Label)}</div>");
                html.Append($"<div class=\"value\">{E(metric.Value)}</div>");
                if (!string.IsNullOrEmpty(metric.Change))
                {
                    html.Append($"<div class=\"change {E(metric.ChangeStatus ?? string.Empty)}\">{E(metric.Change)}</div>");
                }

                html.Append("</div>");
            }

            html.Append("</div>");
        }

        var charts = section.Charts.Where(HasData).ToList();
        if (charts.Count > 0)
        {
            html.Append("<div class=\"charts\">");
            foreach (var chart in charts)
            {
                html.Append($"<figure class=\"chart{(chart.Half ? " half" : string.Empty)}\"><figcaption>{E(chart.Title)}</figcaption>");
                html.Append(Legend(chart));
                html.Append(chart.Kind switch
                {
                    ReportChartKinds.Bar => BarSvg(chart),
                    ReportChartKinds.Pie => PieSvg(chart),
                    ReportChartKinds.Timeline => TimelineSvg(chart),
                    ReportChartKinds.Heatmap => HeatmapSvg(chart),
                    _ => LineSvg(chart)
                });
                html.Append("</figure>");
            }

            html.Append("</div>");
        }

        if (section.Columns.Count > 0)
        {
            html.Append("<table class=\"data\"><thead><tr>");
            foreach (var column in section.Columns)
            {
                html.Append($"<th>{E(column)}</th>");
            }

            html.Append("</tr></thead><tbody>");
            if (section.Rows.Count == 0)
            {
                html.Append($"<tr><td colspan=\"{section.Columns.Count}\" class=\"empty\">Nothing in this period.</td></tr>");
            }

            foreach (var row in section.Rows)
            {
                html.Append("<tr>");
                foreach (var cell in row)
                {
                    html.Append($"<td{CellClass(cell)}>{E(cell)}</td>");
                }

                html.Append("</tr>");
            }

            html.Append("</tbody></table>");
        }

        html.Append("</section>");
    }

    public static bool HasData(ReportChart chart) => chart.Kind switch
    {
        ReportChartKinds.Line => chart.Series.Any(series => series.Values.Count > 1),
        ReportChartKinds.Pie => chart.Series.Count > 0 && chart.Series[0].Values.Any(value => value > 0),
        ReportChartKinds.Timeline or ReportChartKinds.Heatmap => chart.Series.Any(series => series.Values.Any(value => value >= 0)),
        _ => chart.Labels.Count > 0 && chart.Series.Any(series => series.Values.Any(value => value > 0))
    };

    private static string Legend(ReportChart chart)
    {
        var legend = new StringBuilder("<div class=\"legend\">");
        switch (chart.Kind)
        {
            case ReportChartKinds.Timeline:
                foreach (var state in new[] { 2d, 1d, 0d, -1d })
                {
                    legend.Append($"<span><i class=\"box\" style=\"background:{ReportPalette.State(state)}\"></i>{ReportPalette.StateName(state)}</span>");
                }

                break;
            case ReportChartKinds.Heatmap:
                var max = HeatMaximum(chart);
                legend.Append($"<span>Low <i class=\"scale\"></i> High (0 - {ReportPalette.Number(max)}{E(Unit(chart))})</span>");
                break;
            case ReportChartKinds.Pie:
                break;
            default:
                if (chart.Series.Count > 1 || chart.Thresholds.Count > 0)
                {
                    for (var s = 0; s < chart.Series.Count; s++)
                    {
                        var shape = chart.Kind == ReportChartKinds.Bar ? "box" : "line";
                        legend.Append($"<span><i class=\"{shape}\" style=\"background:{ReportPalette.SeriesColor(s)}\"></i>{E(chart.Series[s].Name)}</span>");
                    }

                    foreach (var threshold in chart.Thresholds)
                    {
                        legend.Append($"<span><i class=\"dash\" style=\"border-color:{ReportPalette.Status(threshold.Status)}\"></i>{E(threshold.Name)}</span>");
                    }
                }

                break;
        }

        return legend.Append("</div>").ToString();
    }

    private const int Top = 18;
    private const int Left = 46;
    private const int Bottom = 24;
    private const int Right = 12;

    private static string LineSvg(ReportChart chart)
    {
        var width = chart.Half ? 440 : 900;
        const int height = 210;
        var max = Scale(chart);
        var svg = Open(width, height);
        Axis(svg, width, height, max, chart.Unit);
        var plotWidth = width - Left - Right;
        var plotHeight = height - Top - Bottom;
        var count = chart.Labels.Count;
        double X(int i) => Left + (count <= 1 ? 0 : i * (double)plotWidth / (count - 1));
        double Y(double value) => Top + plotHeight - Math.Clamp(value / max, 0, 1) * plotHeight;

        for (var s = 0; s < chart.Series.Count; s++)
        {
            var values = chart.Series[s].Values;
            if (values.Count < 2)
            {
                continue;
            }

            var color = ReportPalette.SeriesColor(s);
            var points = string.Join(' ', values.Select((value, i) => F($"{X(i):0.#},{Y(value):0.#}")));
            if (chart.Series.Count == 1)
            {
                svg.Append(F($"<polygon fill=\"{color}\" fill-opacity=\"0.12\" points=\"{X(0):0.#},{Top + plotHeight} {points} {X(values.Count - 1):0.#},{Top + plotHeight}\"/>"));
            }

            svg.Append($"<polyline fill=\"none\" stroke=\"{color}\" stroke-width=\"1.6\" stroke-linejoin=\"round\" points=\"{points}\"/>");
        }

        Thresholds(svg, chart, width, Y);
        XLabels(svg, chart.Labels, height, X, chart.Half ? 4 : 7);
        return svg.Append("</svg>").ToString();
    }

    private static string BarSvg(ReportChart chart)
    {
        var width = chart.Half ? 440 : 900;
        const int height = 210;
        var max = Scale(chart);
        var svg = Open(width, height);
        Axis(svg, width, height, max, chart.Unit);
        var plotWidth = width - Left - Right;
        var plotHeight = height - Top - Bottom;
        var groups = chart.Labels.Count;
        var group = plotWidth / (double)groups;
        var bar = Math.Max(1, group * 0.8 / chart.Series.Count);
        double Y(double value) => Top + plotHeight - Math.Clamp(value / max, 0, 1) * plotHeight;

        for (var s = 0; s < chart.Series.Count; s++)
        {
            var color = ReportPalette.SeriesColor(s);
            var values = chart.Series[s].Values;
            for (var i = 0; i < Math.Min(groups, values.Count); i++)
            {
                if (values[i] <= 0)
                {
                    continue;
                }

                var x = Left + i * group + group * 0.1 + s * bar;
                var y = Y(values[i]);
                svg.Append(F($"<rect x=\"{x:0.#}\" y=\"{y:0.#}\" width=\"{bar:0.#}\" height=\"{Top + plotHeight - y:0.#}\" fill=\"{color}\" rx=\"1\">"));
                svg.Append($"<title>{E(chart.Labels[i])}: {ReportPalette.Number(values[i])}{E(Unit(chart))}</title></rect>");
            }
        }

        if (chart.Series.Count == 1 && groups <= (chart.Half ? 12 : 24))
        {
            var values = chart.Series[0].Values;
            for (var i = 0; i < Math.Min(groups, values.Count); i++)
            {
                if (values[i] > 0)
                {
                    svg.Append(F($"<text x=\"{Left + (i + 0.5) * group:0.#}\" y=\"{Y(values[i]) - 3:0.#}\" class=\"v\">{ReportPalette.Number(values[i])}</text>"));
                }
            }
        }

        Thresholds(svg, chart, width, Y);
        XLabels(svg, chart.Labels, height, i => Left + (i + 0.5) * group, chart.Half ? 8 : 16, centered: true);
        return svg.Append("</svg>").ToString();
    }

    private static string PieSvg(ReportChart chart)
    {
        var width = chart.Half ? 440 : 900;
        const int height = 210;
        var values = chart.Series[0].Values;
        var total = values.Where(value => value > 0).Sum();
        var svg = Open(width, height);
        const double cx = 105, cy = 105, outer = 92, inner = 52;
        var angle = -Math.PI / 2;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] <= 0)
            {
                continue;
            }

            var sweep = values[i] / total * Math.PI * 2;
            var color = ReportPalette.SeriesColor(i);
            var label = i < chart.Labels.Count ? chart.Labels[i] : string.Empty;
            if (sweep >= Math.PI * 2 - 0.0001)
            {
                svg.Append(F($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{(outer + inner) / 2:0.#}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{outer - inner:0.#}\"><title>{E(label)}</title></circle>"));
            }
            else
            {
                var end = angle + sweep;
                var large = sweep > Math.PI ? 1 : 0;
                svg.Append(F($"<path fill=\"{color}\" stroke=\"#fff\" stroke-width=\"1\" d=\"M{cx + outer * Math.Cos(angle):0.##},{cy + outer * Math.Sin(angle):0.##} "));
                svg.Append(F($"A{outer},{outer} 0 {large} 1 {cx + outer * Math.Cos(end):0.##},{cy + outer * Math.Sin(end):0.##} "));
                svg.Append(F($"L{cx + inner * Math.Cos(end):0.##},{cy + inner * Math.Sin(end):0.##} "));
                svg.Append(F($"A{inner},{inner} 0 {large} 0 {cx + inner * Math.Cos(angle):0.##},{cy + inner * Math.Sin(angle):0.##} Z\"><title>{E(label)}</title></path>"));
                angle = end;
            }
        }

        svg.Append(F($"<text x=\"{cx}\" y=\"{cy + 5}\" class=\"total\">{ReportPalette.Number(total)}{E(Unit(chart))}</text>"));
        var rows = Math.Min(values.Count, 9);
        for (var i = 0; i < rows; i++)
        {
            var y = 105 - rows * 11 + i * 22 + 11;
            var label = i < chart.Labels.Count ? chart.Labels[i] : string.Empty;
            var share = total > 0 ? values[i] / total * 100 : 0;
            svg.Append($"<rect x=\"220\" y=\"{y - 9}\" width=\"12\" height=\"12\" rx=\"2\" fill=\"{ReportPalette.SeriesColor(i)}\"/>");
            svg.Append($"<text x=\"240\" y=\"{y + 1}\" class=\"l\">{E(Shorten(label, chart.Half ? 18 : 60))}</text>");
            svg.Append(F($"<text x=\"{width - 8}\" y=\"{y + 1}\" class=\"r\">{ReportPalette.Number(values[i])}{E(Unit(chart))} ({share:0.#}%)</text>"));
        }

        return svg.Append("</svg>").ToString();
    }

    private static string TimelineSvg(ReportChart chart)
    {
        var width = chart.Half ? 440 : 900;
        var nameWidth = chart.Half ? 100 : 150;
        const int rowHeight = 20;
        var height = Top + chart.Series.Count * (rowHeight + 4) + Bottom;
        var svg = Open(width, height);
        var plotWidth = width - nameWidth - Right;
        var count = Math.Max(1, chart.Labels.Count);
        var cell = plotWidth / (double)count;
        for (var s = 0; s < chart.Series.Count; s++)
        {
            var y = Top + s * (rowHeight + 4);
            var values = chart.Series[s].Values;
            svg.Append($"<text x=\"{nameWidth - 6}\" y=\"{y + 14}\" class=\"r\">{E(Shorten(chart.Series[s].Name, chart.Half ? 14 : 24))}</text>");
            for (var i = 0; i < values.Count;)
            {
                var j = i;
                while (j + 1 < values.Count && values[j + 1] == values[i])
                {
                    j++;
                }

                var from = i < chart.Labels.Count ? chart.Labels[i] : string.Empty;
                var to = j < chart.Labels.Count ? chart.Labels[j] : string.Empty;
                svg.Append(F($"<rect x=\"{nameWidth + i * cell:0.#}\" y=\"{y}\" width=\"{(j - i + 1) * cell + 0.3:0.#}\" height=\"{rowHeight}\" fill=\"{ReportPalette.State(values[i])}\">"));
                svg.Append($"<title>{E(ReportPalette.StateName(values[i]))}: {E(from)} - {E(to)}</title></rect>");
                i = j + 1;
            }
        }

        XLabels(svg, chart.Labels, height, i => nameWidth + (i + 0.5) * cell, chart.Half ? 4 : 7);
        return svg.Append("</svg>").ToString();
    }

    private static string HeatmapSvg(ReportChart chart)
    {
        var width = chart.Half ? 440 : 900;
        var nameWidth = chart.Half ? 70 : 100;
        const int rowHeight = 18;
        var height = Top + chart.Series.Count * (rowHeight + 2) + Bottom;
        var svg = Open(width, height);
        var max = HeatMaximum(chart);
        var count = Math.Max(1, chart.Labels.Count);
        var cell = (width - nameWidth - Right) / (double)count;
        for (var s = 0; s < chart.Series.Count; s++)
        {
            var y = Top + s * (rowHeight + 2);
            svg.Append($"<text x=\"{nameWidth - 6}\" y=\"{y + 13}\" class=\"r\">{E(Shorten(chart.Series[s].Name, 14))}</text>");
            var values = chart.Series[s].Values;
            for (var i = 0; i < values.Count; i++)
            {
                var x = nameWidth + i * cell;
                var label = i < chart.Labels.Count ? chart.Labels[i] : string.Empty;
                svg.Append(F($"<rect x=\"{x:0.#}\" y=\"{y}\" width=\"{cell - 1.5:0.#}\" height=\"{rowHeight}\" rx=\"2\" fill=\"{ReportPalette.Heat(values[i], max)}\">"));
                svg.Append($"<title>{E(chart.Series[s].Name)} {E(label)}: {(values[i] < 0 ? "no reading" : ReportPalette.Number(values[i]) + E(Unit(chart)))}</title></rect>");
                if (values[i] >= 0 && cell >= 26)
                {
                    svg.Append(F($"<text x=\"{x + cell / 2 - 0.75:0.#}\" y=\"{y + 12.5}\" class=\"cell\">{ReportPalette.Number(values[i])}</text>"));
                }
            }
        }

        XLabels(svg, chart.Labels, height, i => nameWidth + (i + 0.5) * cell, chart.Half ? 12 : 24, centered: true);
        return svg.Append("</svg>").ToString();
    }

    private static StringBuilder Open(int width, int height) =>
        new($"<svg viewBox=\"0 0 {width} {height}\" width=\"100%\" xmlns=\"http://www.w3.org/2000/svg\">");

    private static void Axis(StringBuilder svg, int width, int height, double max, string unit)
    {
        var plotHeight = height - Top - Bottom;
        for (var i = 0; i <= 4; i++)
        {
            var y = Top + plotHeight - plotHeight * i / 4d;
            svg.Append(F($"<line x1=\"{Left}\" y1=\"{y:0.#}\" x2=\"{width - Right}\" y2=\"{y:0.#}\" stroke=\"{(i == 0 ? "#bbb" : "#eee")}\"/>"));
            svg.Append(F($"<text x=\"{Left - 5}\" y=\"{y + 3:0.#}\" class=\"r\">{ReportPalette.Number(max * i / 4)}</text>"));
        }

        if (!string.IsNullOrEmpty(unit))
        {
            svg.Append($"<text x=\"2\" y=\"9\" class=\"u\">{E(unit)}</text>");
        }
    }

    private static void Thresholds(StringBuilder svg, ReportChart chart, int width, Func<double, double> y)
    {
        var max = Scale(chart);
        foreach (var threshold in chart.Thresholds.Where(t => t.Value > 0 && t.Value <= max))
        {
            var color = ReportPalette.Status(threshold.Status);
            var top = y(threshold.Value);
            svg.Append(F($"<line x1=\"{Left}\" y1=\"{top:0.#}\" x2=\"{width - Right}\" y2=\"{top:0.#}\" stroke=\"{color}\" stroke-width=\"1.2\" stroke-dasharray=\"5 3\"/>"));
            svg.Append(F($"<text x=\"{width - Right - 2}\" y=\"{top - 3:0.#}\" class=\"r\" fill=\"{color}\">{E(threshold.Name)}</text>"));
        }
    }

    private static void XLabels(StringBuilder svg, IReadOnlyList<string> labels, int height, Func<int, double> x, int most, bool centered = false)
    {
        if (labels.Count == 0)
        {
            return;
        }

        var step = Math.Max(1, (int)Math.Ceiling(labels.Count / (double)most));
        for (var i = 0; i < labels.Count; i += step)
        {
            if (!centered && i > 0 && i != labels.Count - 1 && x(labels.Count - 1) - x(i) < 100)
            {
                continue;
            }

            var anchor = centered ? "middle" : i == 0 ? "start" : "middle";
            svg.Append(F($"<text x=\"{x(i):0.#}\" y=\"{height - 7}\" class=\"x\" text-anchor=\"{anchor}\">{E(labels[i])}</text>"));
        }

        if (!centered && (labels.Count - 1) % step != 0 && labels.Count > 1)
        {
            svg.Append(F($"<text x=\"{x(labels.Count - 1):0.#}\" y=\"{height - 7}\" class=\"x\" text-anchor=\"end\">{E(labels[^1])}</text>"));
        }
    }

    private static double Scale(ReportChart chart)
    {
        if (chart.Maximum > 0)
        {
            return chart.Maximum;
        }

        return ReportPalette.NiceMaximum(chart.Series.SelectMany(series => series.Values)
            .Concat(chart.Thresholds.Select(threshold => threshold.Value)));
    }

    private static double HeatMaximum(ReportChart chart) =>
        chart.Maximum > 0 ? chart.Maximum : ReportPalette.NiceMaximum(chart.Series.SelectMany(series => series.Values));

    private static string Unit(ReportChart chart) =>
        string.IsNullOrEmpty(chart.Unit) ? string.Empty : chart.Unit is "%" ? "%" : $" {chart.Unit}";

    private static string CellClass(string cell) => cell switch
    {
        "Working" or "Healthy" or "Up" or "Online" or "Pass" or "Good" or "Yes" or "Resolved" => " class=\"Good\"",
        "Warning" or "Degraded" or "Slow" => " class=\"Warning\"",
        "Down" or "Problem" or "Offline" or "Fail" or "Error" or "Unreachable" or "Open" => " class=\"Bad\"",
        _ => string.Empty
    };

    private static string Length(TimeSpan span) => span.TotalDays >= 1
        ? $"{span.TotalDays:0.#} days"
        : $"{span.TotalHours:0.#} hours";

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    private static string? LoadLogo()
    {
        using var stream = typeof(ReportExporter).Assembly.GetManifestResourceStream("ClientAgent.Shared.Logo.png");
        if (stream is null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Convert.ToBase64String(memory.ToArray());
    }

    private static string Invariant(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string F(FormattableString text) => FormattableString.Invariant(text);

    private static string Row(IEnumerable<string> cells) =>
        string.Join(',', cells.Select(cell => cell.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{cell.Replace("\"", "\"\"")}\"" : cell));

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private static string Css(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");

    private const string Style = """
        :root{--accent:#2e7d32}
        *{box-sizing:border-box}
        body{font-family:'Segoe UI',Arial,sans-serif;color:#222;margin:24px auto;max-width:1000px;font-size:12.5px;-webkit-print-color-adjust:exact;print-color-adjust:exact}
        .cover{display:flex;align-items:center;gap:16px;padding:14px 18px;border-radius:10px;background:linear-gradient(90deg,#1b5e20,#2e7d32 60%,#43a047);color:#fff}
        .cover .logo{width:64px;height:64px;border-radius:12px;background:#fff;padding:6px}
        .cover .program{font-size:12px;letter-spacing:2px;text-transform:uppercase;opacity:.85}
        .cover h1{margin:2px 0 4px;font-size:25px}
        .cover .desc{opacity:.92}
        .facts{width:100%;border-collapse:separate;border-spacing:6px;margin:8px -6px 4px}
        .facts td{background:#f4f7f4;border:1px solid #e0e8e0;border-radius:6px;padding:6px 10px;font-weight:600}
        .facts span{display:block;font-size:10px;font-weight:400;color:#6b7a6b;text-transform:uppercase;letter-spacing:.5px}
        .toc{border:1px solid #e0e8e0;border-radius:8px;padding:8px 14px;margin:10px 0}
        .toc ol{margin:4px 0 0;padding-left:20px;columns:3} .toc a{color:#1b5e20;text-decoration:none}
        section{margin-top:18px}
        h2{font-size:16px;margin:0 0 6px;padding:6px 10px;border-left:5px solid var(--accent);background:#f4f7f4;border-radius:0 6px 6px 0;break-after:avoid}
        .note{color:#666;margin:4px 0 8px}
        .metrics{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:8px;margin:8px 0}
        .metric{border:1px solid #e0e0e0;border-top:3px solid #9e9e9e;border-radius:6px;padding:7px 10px;break-inside:avoid}
        .metric.Good{border-top-color:#2e7d32} .metric.Warning{border-top-color:#f9a825} .metric.Bad{border-top-color:#c62828}
        .metric .label{color:#666;font-size:10.5px} .metric .value{font-size:17px;font-weight:600}
        .metric.Good .value{color:#2e7d32} .metric.Warning .value{color:#b07800} .metric.Bad .value{color:#c62828}
        .metric .change{font-size:10.5px;color:#666}
        .Good{color:#2e7d32} .Warning{color:#b07800} .Bad{color:#c62828}
        .charts{display:flex;flex-wrap:wrap;gap:10px;margin:8px 0}
        .chart{flex:1 1 100%;margin:0;border:1px solid #e3e3e3;border-radius:8px;padding:8px 10px 4px;break-inside:avoid}
        .chart.half{flex:1 1 calc(50% - 10px);max-width:calc(50% - 5px)}
        figcaption{font-weight:600;font-size:12.5px;margin-bottom:2px}
        .legend{font-size:10.5px;color:#555;margin-bottom:2px;min-height:4px} .legend span{margin-right:12px;white-space:nowrap}
        .legend i{display:inline-block;vertical-align:middle;margin-right:4px}
        .legend .line{width:16px;height:3px} .legend .box{width:10px;height:10px;border-radius:2px}
        .legend .dash{width:16px;border-top:2px dashed} .legend .scale{width:70px;height:9px;background:linear-gradient(90deg,#43a047,#fdd835,#e53935);margin:0 4px}
        svg text{font-family:'Segoe UI',Arial,sans-serif;font-size:10px;fill:#666}
        svg .r{text-anchor:end} svg .u{font-style:italic} svg .x{fill:#777} svg .v{text-anchor:middle;font-size:9px;fill:#444}
        svg .l{fill:#333;font-size:11px} svg .total{text-anchor:middle;font-size:15px;font-weight:600;fill:#333}
        svg .cell{text-anchor:middle;font-size:8.5px;fill:#222}
        table.data{border-collapse:collapse;width:100%;margin:8px 0;font-size:11.5px}
        table.data th,table.data td{border:1px solid #e0e0e0;padding:4px 7px;text-align:left;vertical-align:top}
        table.data th{background:#2e7d32;color:#fff;font-weight:600}
        table.data tr:nth-child(even) td{background:#f8faf8}
        table.data td.empty{color:#888;text-align:center}
        thead{display:table-header-group} tr{break-inside:avoid}
        .print{position:fixed;top:14px;right:14px;padding:8px 16px;background:#2e7d32;color:#fff;border:0;border-radius:6px;cursor:pointer;box-shadow:0 2px 6px rgba(0,0,0,.25)}
        .end{margin-top:24px;padding-top:8px;border-top:1px solid #ddd;color:#888;font-size:10.5px;text-align:center}
        @media print{.print{display:none} body{margin:0;max-width:none} .toc{display:none} section{break-inside:auto} .cover{border-radius:0}}

        """;
}
