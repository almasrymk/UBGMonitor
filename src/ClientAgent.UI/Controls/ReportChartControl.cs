using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ClientAgent.Shared.Models.Reports;
using ClientAgent.Shared.Reports;
using ClientAgent.UI.Services;

namespace ClientAgent.UI.Controls;

/// <summary>Draws one report chart (line, bar, pie, timeline or heatmap); hovering shows the values under the mouse.</summary>
public sealed class ReportChartControl : FrameworkElement
{
    private const double Left = 46;
    private const double Top = 18;
    private const double Bottom = 22;
    private const double Right = 10;
    private const double RowHeight = 20;
    private const double ChartHeight = 200;

    private static readonly Typeface Face = new("Segoe UI");

    private Point? _mouse;

    public ReportChartControl()
    {
        UiTheme.Changed += (_, _) => InvalidateVisual();
        Cursor = Cursors.Cross;
    }

    public static readonly DependencyProperty ChartProperty = DependencyProperty.Register(
        nameof(Chart), typeof(ReportChart), typeof(ReportChartControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public ReportChart? Chart
    {
        get => (ReportChart?)GetValue(ChartProperty);
        set => SetValue(ChartProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var chart = Chart;
        var width = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        if (chart is null)
        {
            return new Size(0, 0);
        }

        var height = chart.Kind switch
        {
            ReportChartKinds.Timeline => Top + chart.Series.Count * (RowHeight + 4) + Bottom,
            ReportChartKinds.Heatmap => Top + chart.Series.Count * (RowHeight - 2 + 2) + Bottom,
            _ => ChartHeight
        };
        return new Size(width, height);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _mouse = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _mouse = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var chart = Chart;
        if (chart is null || ActualWidth < 60 || !ReportExporter.HasData(chart))
        {
            return;
        }

        switch (chart.Kind)
        {
            case ReportChartKinds.Bar:
                DrawBars(dc, chart);
                break;
            case ReportChartKinds.Pie:
                DrawPie(dc, chart);
                break;
            case ReportChartKinds.Timeline:
                DrawTimeline(dc, chart);
                break;
            case ReportChartKinds.Heatmap:
                DrawHeatmap(dc, chart);
                break;
            default:
                DrawLines(dc, chart);
                break;
        }
    }

    private void DrawLines(DrawingContext dc, ReportChart chart)
    {
        var max = Scale(chart);
        var plot = Plot();
        DrawAxis(dc, plot, max, chart.Unit);
        var count = chart.Labels.Count;
        double X(int i) => plot.Left + (count <= 1 ? 0 : i * plot.Width / (count - 1));
        double Y(double value) => plot.Bottom - Math.Clamp(value / max, 0, 1) * plot.Height;

        for (var s = 0; s < chart.Series.Count; s++)
        {
            var values = chart.Series[s].Values;
            if (values.Count < 2)
            {
                continue;
            }

            var color = Color(ReportPalette.SeriesColor(s));
            var line = new StreamGeometry();
            using (var context = line.Open())
            {
                context.BeginFigure(new Point(X(0), Y(values[0])), false, false);
                for (var i = 1; i < values.Count; i++)
                {
                    context.LineTo(new Point(X(i), Y(values[i])), true, true);
                }
            }

            if (chart.Series.Count == 1)
            {
                var area = new StreamGeometry();
                using (var context = area.Open())
                {
                    context.BeginFigure(new Point(X(0), plot.Bottom), true, true);
                    for (var i = 0; i < values.Count; i++)
                    {
                        context.LineTo(new Point(X(i), Y(values[i])), false, false);
                    }

                    context.LineTo(new Point(X(values.Count - 1), plot.Bottom), false, false);
                }

                dc.DrawGeometry(new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, color.R, color.G, color.B)), null, area);
            }

            dc.DrawGeometry(null, new Pen(new SolidColorBrush(color), 1.6) { LineJoin = PenLineJoin.Round }, line);
        }

        DrawThresholds(dc, chart, plot, Y);
        DrawXLabels(dc, chart.Labels, X, plot, centered: false);

        if (_mouse is { } mouse && plot.Contains(mouse) && count > 1)
        {
            var index = (int)Math.Round((mouse.X - plot.Left) / plot.Width * (count - 1));
            index = Math.Clamp(index, 0, count - 1);
            dc.DrawLine(new Pen(Secondary(), 1) { DashStyle = DashStyles.Dot }, new Point(X(index), plot.Top), new Point(X(index), plot.Bottom));
            var lines = chart.Series.Where(series => index < series.Values.Count)
                .Select(series => $"{series.Name}: {ReportPalette.Number(series.Values[index])}{Unit(chart)}");
            DrawHover(dc, mouse, [chart.Labels[index], .. lines]);
        }
    }

    private void DrawBars(DrawingContext dc, ReportChart chart)
    {
        var max = Scale(chart);
        var plot = Plot();
        DrawAxis(dc, plot, max, chart.Unit);
        var groups = chart.Labels.Count;
        var group = plot.Width / groups;
        var bar = Math.Max(1, group * 0.8 / chart.Series.Count);
        double Y(double value) => plot.Bottom - Math.Clamp(value / max, 0, 1) * plot.Height;
        var hovered = _mouse is { } m && plot.Contains(m) ? Math.Clamp((int)((m.X - plot.Left) / group), 0, groups - 1) : -1;
        if (hovered >= 0)
        {
            dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 0x80, 0x80, 0x80)), null,
                new Rect(plot.Left + hovered * group, plot.Top, group, plot.Height));
        }

        for (var s = 0; s < chart.Series.Count; s++)
        {
            var brush = new SolidColorBrush(Color(ReportPalette.SeriesColor(s)));
            var values = chart.Series[s].Values;
            for (var i = 0; i < Math.Min(groups, values.Count); i++)
            {
                if (values[i] <= 0)
                {
                    continue;
                }

                var y = Y(values[i]);
                dc.DrawRectangle(brush, null, new Rect(plot.Left + i * group + group * 0.1 + s * bar, y, bar, Math.Max(0.5, plot.Bottom - y)));
            }
        }

        DrawThresholds(dc, chart, plot, Y);
        DrawXLabels(dc, chart.Labels, i => plot.Left + (i + 0.5) * group, plot, centered: true);
        if (hovered >= 0 && _mouse is { } mouse)
        {
            var lines = chart.Series.Where(series => hovered < series.Values.Count)
                .Select(series => $"{series.Name}: {ReportPalette.Number(series.Values[hovered])}{Unit(chart)}");
            DrawHover(dc, mouse, [chart.Labels[hovered], .. lines]);
        }
    }

    private void DrawPie(DrawingContext dc, ReportChart chart)
    {
        var values = chart.Series[0].Values;
        var total = values.Where(v => v > 0).Sum();
        var radius = Math.Min(ActualHeight, ActualWidth / 2) / 2 - 8;
        var inner = radius * 0.56;
        var center = new Point(radius + 10, ActualHeight / 2);
        var angle = -Math.PI / 2;
        var hovered = -1;
        if (_mouse is { } m)
        {
            var dx = m.X - center.X;
            var dy = m.Y - center.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance >= inner && distance <= radius)
            {
                var at = Math.Atan2(dy, dx) + Math.PI / 2;
                at = at < 0 ? at + Math.PI * 2 : at;
                var sum = 0d;
                for (var i = 0; i < values.Count; i++)
                {
                    sum += Math.Max(0, values[i]) / total * Math.PI * 2;
                    if (at <= sum)
                    {
                        hovered = i;
                        break;
                    }
                }
            }
        }

        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] <= 0)
            {
                continue;
            }

            var sweep = Math.Min(values[i] / total * Math.PI * 2, Math.PI * 2 - 0.0001);
            var grow = i == hovered ? 4 : 0;
            var geometry = Slice(center, radius + grow, inner, angle, sweep);
            dc.DrawGeometry(new SolidColorBrush(Color(ReportPalette.SeriesColor(i))), new Pen(Background(), 1), geometry);
            angle += sweep;
        }

        Text(dc, $"{ReportPalette.Number(total)}{Unit(chart)}", center.X, center.Y - 9, 14, Primary(), TextAlignment.Center, bold: true);

        var x = center.X + radius + 22;
        var rows = Math.Min(values.Count, 9);
        var rowHeight = Math.Min(20, (ActualHeight - 8) / Math.Max(1, rows));
        var y = ActualHeight / 2 - rows * rowHeight / 2;
        for (var i = 0; i < rows; i++)
        {
            var label = i < chart.Labels.Count ? chart.Labels[i] : string.Empty;
            var share = total > 0 ? values[i] / total * 100 : 0;
            dc.DrawRoundedRectangle(new SolidColorBrush(Color(ReportPalette.SeriesColor(i))), null, new Rect(x, y + 4, 11, 11), 2, 2);
            Text(dc, $"{label}  {ReportPalette.Number(values[i])}{Unit(chart)} ({share:0.#}%)", x + 17, y + 1, 11,
                i == hovered ? Primary() : Secondary(), TextAlignment.Left, bold: i == hovered, maxWidth: ActualWidth - x - 20);
            y += rowHeight;
        }
    }

    private void DrawTimeline(DrawingContext dc, ReportChart chart)
    {
        var nameWidth = Math.Min(150, ActualWidth * 0.25);
        var plot = new Rect(nameWidth, Top, Math.Max(10, ActualWidth - nameWidth - Right), chart.Series.Count * (RowHeight + 4));
        var count = Math.Max(1, chart.Labels.Count);
        var cell = plot.Width / count;
        for (var s = 0; s < chart.Series.Count; s++)
        {
            var y = Top + s * (RowHeight + 4);
            Text(dc, chart.Series[s].Name, nameWidth - 6, y + 2, 11, Primary(), TextAlignment.Right, maxWidth: nameWidth - 8);
            var values = chart.Series[s].Values;
            for (var i = 0; i < values.Count;)
            {
                var j = i;
                while (j + 1 < values.Count && values[j + 1] == values[i])
                {
                    j++;
                }

                dc.DrawRectangle(StateBrush(values[i]), null, new Rect(plot.Left + i * cell, y, (j - i + 1) * cell + 0.4, RowHeight));
                i = j + 1;
            }
        }

        DrawXLabels(dc, chart.Labels, i => plot.Left + (i + 0.5) * cell, plot, centered: false);
        if (_mouse is { } mouse && plot.Contains(mouse))
        {
            var index = Math.Clamp((int)((mouse.X - plot.Left) / cell), 0, count - 1);
            var row = Math.Clamp((int)((mouse.Y - Top) / (RowHeight + 4)), 0, chart.Series.Count - 1);
            var series = chart.Series[row];
            dc.DrawLine(new Pen(Primary(), 1), new Point(plot.Left + (index + 0.5) * cell, plot.Top), new Point(plot.Left + (index + 0.5) * cell, plot.Bottom));
            var state = index < series.Values.Count ? series.Values[index] : -1;
            DrawHover(dc, mouse, [series.Name, index < chart.Labels.Count ? chart.Labels[index] : string.Empty, ReportPalette.StateName(state)]);
        }
    }

    private void DrawHeatmap(DrawingContext dc, ReportChart chart)
    {
        var nameWidth = Math.Min(100, ActualWidth * 0.2);
        var max = chart.Maximum > 0 ? chart.Maximum : ReportPalette.NiceMaximum(chart.Series.SelectMany(series => series.Values));
        var count = Math.Max(1, chart.Labels.Count);
        var plot = new Rect(nameWidth, Top, Math.Max(10, ActualWidth - nameWidth - Right), chart.Series.Count * RowHeight);
        var cell = plot.Width / count;
        for (var s = 0; s < chart.Series.Count; s++)
        {
            var y = Top + s * RowHeight;
            Text(dc, chart.Series[s].Name, nameWidth - 6, y + 1, 11, Primary(), TextAlignment.Right, maxWidth: nameWidth - 8);
            var values = chart.Series[s].Values;
            for (var i = 0; i < values.Count; i++)
            {
                var brush = values[i] < 0 ? NoReadingBrush() : new SolidColorBrush(Color(ReportPalette.Heat(values[i], max)));
                dc.DrawRoundedRectangle(brush, null, new Rect(plot.Left + i * cell, y, Math.Max(1, cell - 1.5), RowHeight - 2), 2, 2);
                if (values[i] >= 0 && cell >= 28)
                {
                    Text(dc, ReportPalette.Number(values[i]), plot.Left + i * cell + cell / 2 - 0.75, y + 2, 9, Brushes.Black, TextAlignment.Center);
                }
            }
        }

        DrawXLabels(dc, chart.Labels, i => plot.Left + (i + 0.5) * cell, plot, centered: true);
        if (_mouse is { } mouse && plot.Contains(mouse))
        {
            var index = Math.Clamp((int)((mouse.X - plot.Left) / cell), 0, count - 1);
            var row = Math.Clamp((int)((mouse.Y - Top) / RowHeight), 0, chart.Series.Count - 1);
            var series = chart.Series[row];
            var value = index < series.Values.Count ? series.Values[index] : -1;
            DrawHover(dc, mouse, [$"{series.Name}  {(index < chart.Labels.Count ? chart.Labels[index] : string.Empty)}:00",
                value < 0 ? "No reading" : $"{ReportPalette.Number(value)}{Unit(chart)}"]);
        }
    }

    private Rect Plot() => new(Left, Top, Math.Max(10, ActualWidth - Left - Right), Math.Max(10, ActualHeight - Top - Bottom));

    private void DrawAxis(DrawingContext dc, Rect plot, double max, string unit)
    {
        var grid = new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, 0x90, 0x90, 0x90)), 1);
        var axis = new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0x90, 0x90, 0x90)), 1);
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4;
            dc.DrawLine(i == 0 ? axis : grid, new Point(plot.Left, y), new Point(plot.Right, y));
            Text(dc, ReportPalette.Number(max * i / 4), plot.Left - 5, y - 7, 10, Secondary(), TextAlignment.Right);
        }

        if (!string.IsNullOrEmpty(unit))
        {
            Text(dc, unit, 2, 0, 9.5, Secondary(), TextAlignment.Left);
        }
    }

    private void DrawThresholds(DrawingContext dc, ReportChart chart, Rect plot, Func<double, double> y)
    {
        var max = Scale(chart);
        foreach (var threshold in chart.Thresholds.Where(t => t.Value > 0 && t.Value <= max))
        {
            var brush = new SolidColorBrush(Color(ReportPalette.Status(threshold.Status)));
            var top = y(threshold.Value);
            dc.DrawLine(new Pen(brush, 1.2) { DashStyle = new DashStyle([5, 3], 0) }, new Point(plot.Left, top), new Point(plot.Right, top));
            Text(dc, threshold.Name, plot.Right - 2, top - 14, 9.5, brush, TextAlignment.Right);
        }
    }

    private void DrawXLabels(DrawingContext dc, IReadOnlyList<string> labels, Func<int, double> x, Rect plot, bool centered)
    {
        if (labels.Count == 0)
        {
            return;
        }

        var most = Math.Max(2, (int)(plot.Width / (centered ? 34 : 90)));
        var step = Math.Max(1, (int)Math.Ceiling(labels.Count / (double)most));
        for (var i = 0; i < labels.Count; i += step)
        {
            var alignment = centered || i > 0 ? TextAlignment.Center : TextAlignment.Left;
            var at = x(i);
            if (!centered && i > 0 && at + 40 > ActualWidth)
            {
                continue;
            }

            Text(dc, labels[i], at, plot.Bottom + 4, 10, Secondary(), alignment);
        }
    }

    private void DrawHover(DrawingContext dc, Point mouse, IReadOnlyList<string> lines)
    {
        var texts = lines.Where(line => !string.IsNullOrEmpty(line))
            .Select((line, i) => Format(line, 11, i == 0 ? Secondary() : Primary(), TextAlignment.Left, bold: i > 0))
            .ToList();
        if (texts.Count == 0)
        {
            return;
        }

        var width = texts.Max(text => text.Width) + 16;
        var height = texts.Sum(text => text.Height) + 10;
        var x = mouse.X + 14 + width > ActualWidth ? mouse.X - 14 - width : mouse.X + 14;
        var y = Math.Clamp(mouse.Y - height / 2, 0, Math.Max(0, ActualHeight - height));
        dc.DrawRoundedRectangle(Background(), new Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x90, 0x90, 0x90, 0x90)), 1),
            new Rect(Math.Max(0, x), y, width, height), 4, 4);
        var top = y + 5;
        foreach (var text in texts)
        {
            dc.DrawText(text, new Point(Math.Max(0, x) + 8, top));
            top += text.Height;
        }
    }

    private static Geometry Slice(Point center, double outer, double inner, double start, double sweep)
    {
        var end = start + sweep;
        var large = sweep > Math.PI;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(At(center, outer, start), true, true);
            context.ArcTo(At(center, outer, end), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true, true);
            context.LineTo(At(center, inner, end), true, true);
            context.ArcTo(At(center, inner, start), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point At(Point center, double radius, double angle) =>
        new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));

    private FormattedText Format(string text, double size, Brush brush, TextAlignment alignment, bool bold = false, double maxWidth = 0)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            bold ? new Typeface(Face.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal) : Face,
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            TextAlignment = alignment,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };
        if (maxWidth > 0)
        {
            formatted.MaxTextWidth = maxWidth;
        }

        return formatted;
    }

    private void Text(DrawingContext dc, string text, double x, double y, double size, Brush brush, TextAlignment alignment,
        bool bold = false, double maxWidth = 0)
    {
        var formatted = Format(text, size, brush, alignment, bold, maxWidth);
        if (maxWidth > 0)
        {
            x = alignment switch
            {
                TextAlignment.Right => x - maxWidth,
                TextAlignment.Center => x - maxWidth / 2,
                _ => x
            };
        }

        dc.DrawText(formatted, new Point(x, y));
    }

    private static double Scale(ReportChart chart) => chart.Maximum > 0
        ? chart.Maximum
        : ReportPalette.NiceMaximum(chart.Series.SelectMany(series => series.Values).Concat(chart.Thresholds.Select(t => t.Value)));

    private static string Unit(ReportChart chart) =>
        string.IsNullOrEmpty(chart.Unit) ? string.Empty : chart.Unit == "%" ? "%" : $" {chart.Unit}";

    private static Brush StateBrush(double state) =>
        state < 0 ? NoReadingBrush() : new SolidColorBrush(Color(ReportPalette.State(state)));

    private static Brush NoReadingBrush() => new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0x90, 0x90, 0x90));

    private static Brush Primary() => UiTheme.Brush("TextPrimaryBrush", Colors.White);

    private static Brush Secondary() => UiTheme.Brush("TextSecondaryBrush", System.Windows.Media.Color.FromRgb(0xB0, 0xB0, 0xB0));

    private static Brush Background() => UiTheme.Brush("SurfaceDarkBrush", System.Windows.Media.Color.FromRgb(0x25, 0x25, 0x26));

    private static Color Color(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
