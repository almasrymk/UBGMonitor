using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using MonitorAgent.UI.Services;

namespace MonitorAgent.Desktop.Controls;

public sealed class ActivityGraphControl : ThemedControl
{
    public static readonly StyledProperty<IEnumerable?> ValuesProperty =
        AvaloniaProperty.Register<ActivityGraphControl, IEnumerable?>(nameof(Values));

    public static readonly StyledProperty<IEnumerable?> SecondaryValuesProperty =
        AvaloniaProperty.Register<ActivityGraphControl, IEnumerable?>(nameof(SecondaryValues));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<ActivityGraphControl, IBrush?>(nameof(Stroke), Brushes.LimeGreen);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<ActivityGraphControl, double>(nameof(Maximum));

    public static readonly StyledProperty<int> CapacityProperty =
        AvaloniaProperty.Register<ActivityGraphControl, int>(nameof(Capacity), 60);

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<ActivityGraphControl, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<ActivityGraphControl, string>(nameof(Unit), "%");

    public static readonly StyledProperty<bool> FillAreaProperty =
        AvaloniaProperty.Register<ActivityGraphControl, bool>(nameof(FillArea), true);

    /// <summary>When false, draws only the series (no grid, border, or labels).</summary>
    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<ActivityGraphControl, bool>(nameof(ShowGrid), true);

    /// <summary>Current reading drawn as a badge in the top-left corner.</summary>
    public static readonly StyledProperty<string> ValueLabelProperty =
        AvaloniaProperty.Register<ActivityGraphControl, string>(nameof(ValueLabel), string.Empty);

    static ActivityGraphControl()
    {
        AffectsRender<ActivityGraphControl>(ValuesProperty, SecondaryValuesProperty, StrokeProperty, MaximumProperty,
            CapacityProperty, TitleProperty, UnitProperty, FillAreaProperty, ShowGridProperty, ValueLabelProperty);
    }

    public IEnumerable? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public IEnumerable? SecondaryValues { get => GetValue(SecondaryValuesProperty); set => SetValue(SecondaryValuesProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int Capacity { get => GetValue(CapacityProperty); set => SetValue(CapacityProperty, value); }
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public bool FillArea { get => GetValue(FillAreaProperty); set => SetValue(FillAreaProperty, value); }
    public bool ShowGrid { get => GetValue(ShowGridProperty); set => SetValue(ShowGridProperty, value); }
    public string ValueLabel { get => GetValue(ValueLabelProperty); set => SetValue(ValueLabelProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValuesProperty || change.Property == SecondaryValuesProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldSeries)
            {
                oldSeries.CollectionChanged -= OnCollectionChanged;
            }

            if (change.NewValue is INotifyCollectionChanged newSeries)
            {
                newSeries.CollectionChanged += OnCollectionChanged;
            }
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext dc)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 2 || height <= 2)
        {
            return;
        }

        var color = ColorOf(Stroke, Color.FromRgb(0x4C, 0xAF, 0x50));
        var lineBrush = new SolidColorBrush(color);
        var gridPen = new Pen(WithAlpha(color, 0x30), 1);
        var borderPen = new Pen(WithAlpha(color, 0x90), 1);
        var fillBrush = WithAlpha(color, 0x40);
        var labelBrush = UiTheme.Brush("TextSecondaryBrush", Color.FromRgb(0xB0, 0xB0, 0xB0));

        var primary = Read(Values);
        var secondary = Read(SecondaryValues);
        var max = Maximum > 0 ? Maximum : NiceMaximum(primary.Concat(secondary));
        var capacity = Math.Max(2, Capacity);
        var step = width / (capacity - 1);

        using (dc.PushClip(new Rect(0, 0, width, height)))
        {
            if (ShowGrid)
            {
                for (var i = 1; i < 10; i++)
                {
                    var y = Math.Round(height * i / 10d) + 0.5;
                    dc.DrawLine(gridPen, new Point(0, y), new Point(width, y));
                }

                for (var i = 1; i < 6; i++)
                {
                    var x = Math.Round(width * i / 6d) + 0.5;
                    dc.DrawLine(gridPen, new Point(x, 0), new Point(x, height));
                }
            }

            var lineWidth = ShowGrid ? 1.4 : 1.6;
            DrawSeries(dc, primary, max, step, width, height, FillArea ? fillBrush : null,
                new Pen(lineBrush, lineWidth, lineJoin: PenLineJoin.Round));
            DrawSeries(dc, secondary, max, step, width, height, null,
                new Pen(lineBrush, lineWidth - 0.2, new DashStyle([3, 2], 0), lineJoin: PenLineJoin.Round));
        }

        if (ShowGrid)
        {
            dc.DrawRectangle(null, borderPen, new Rect(0.5, 0.5, width - 1, height - 1));
            if (!string.IsNullOrEmpty(Title))
            {
                dc.DrawText(Format(Title, 10, labelBrush), new Point(4, 2));
            }

            if (primary.Count > 0 || secondary.Count > 0)
            {
                var scale = Format(ScaleText(max), 10, labelBrush);
                dc.DrawText(scale, new Point(width - scale.Width - 4, 2));
            }
        }

        if (!string.IsNullOrEmpty(ValueLabel))
        {
            var label = Format(ValueLabel, 10.5, UiTheme.Brush("TextPrimaryBrush", Colors.White), bold: true);
            var background = WithAlpha(ColorOf(UiTheme.Brush("SurfaceDarkBrush", Color.FromRgb(0x1E, 0x1E, 0x1E)), Colors.Black), 0xD9);
            var box = new Rect(1, 1, label.Width + 8, label.Height + 2);
            dc.DrawRectangle(background, null, box, 3, 3);
            dc.DrawText(label, new Point(box.X + 4, box.Y + 1));
        }
    }

    private static void DrawSeries(DrawingContext dc, IReadOnlyList<double> values, double max, double step,
        double width, double height, IBrush? fill, IPen pen)
    {
        if (values.Count < 2)
        {
            return;
        }

        var points = new Point[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            var x = width - (values.Count - 1 - i) * step;
            var y = height - Math.Clamp(values[i] / max, 0, 1) * (height - 1);
            points[i] = new Point(x, y);
        }

        if (fill is not null)
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(points[0].X, height), true);
                foreach (var point in points)
                {
                    ctx.LineTo(point);
                }

                ctx.LineTo(new Point(points[^1].X, height));
                ctx.EndFigure(true);
            }

            dc.DrawGeometry(fill, null, area);
        }

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(points[0], false);
            for (var i = 1; i < points.Length; i++)
            {
                ctx.LineTo(points[i]);
            }

            ctx.EndFigure(false);
        }

        dc.DrawGeometry(null, pen, line);
    }

    private static List<double> Read(IEnumerable? source) =>
        source?.Cast<object>().Select(v => Convert.ToDouble(v, CultureInfo.InvariantCulture)).ToList() ?? [];

    private string ScaleText(double max) => Unit switch
    {
        "%" => $"{max:0}%",
        "MB/s" when max < 1 => $"{max * 1024:0} KB/s",
        "Kbps" when max >= 1000 => $"{max / 1000d:0.##} Mbps",
        _ => $"{max:0.##} {Unit}"
    };

    private static double NiceMaximum(IEnumerable<double> values)
    {
        var peak = values.DefaultIfEmpty(0).Max();
        if (peak <= 0.01)
        {
            return 0.01;
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
}
