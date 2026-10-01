using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MonitorAgent.UI.Services;

namespace MonitorAgent.UI.Controls;

public sealed class ActivityGraphControl : FrameworkElement
{
    public ActivityGraphControl()
    {
        UiTheme.Changed += (_, _) => InvalidateVisual();
    }

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IEnumerable), typeof(ActivityGraphControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSeriesChanged));

    public static readonly DependencyProperty SecondaryValuesProperty = DependencyProperty.Register(
        nameof(SecondaryValues), typeof(IEnumerable), typeof(ActivityGraphControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSeriesChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(ActivityGraphControl),
        new FrameworkPropertyMetadata(Brushes.LimeGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(ActivityGraphControl),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity), typeof(int), typeof(ActivityGraphControl),
        new FrameworkPropertyMetadata(60, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ActivityGraphControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(ActivityGraphControl),
        new FrameworkPropertyMetadata("%", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillAreaProperty = DependencyProperty.Register(
        nameof(FillArea), typeof(bool), typeof(ActivityGraphControl),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>When false, draws only the series (no grid, border, or labels), like <see cref="SparklineControl"/>.</summary>
    public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(
        nameof(ShowGrid), typeof(bool), typeof(ActivityGraphControl),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueLabelProperty = DependencyProperty.Register(
        nameof(ValueLabel), typeof(string), typeof(ActivityGraphControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly List<INotifyCollectionChanged> _subscribed = [];

    /// <summary>Current reading drawn as a badge in the top-left corner.</summary>
    public string ValueLabel
    {
        get => (string)GetValue(ValueLabelProperty);
        set => SetValue(ValueLabelProperty, value);
    }

    public bool ShowGrid
    {
        get => (bool)GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    public bool FillArea
    {
        get => (bool)GetValue(FillAreaProperty);
        set => SetValue(FillAreaProperty, value);
    }

    public IEnumerable? Values
    {
        get => (IEnumerable?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IEnumerable? SecondaryValues
    {
        get => (IEnumerable?)GetValue(SecondaryValuesProperty);
        set => SetValue(SecondaryValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>Fixed top of the scale. 0 scales to the largest visible value.</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int Capacity
    {
        get => (int)GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    private static void OnSeriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ActivityGraphControl)d;
        if (e.OldValue is INotifyCollectionChanged oldSeries)
        {
            oldSeries.CollectionChanged -= control.OnCollectionChanged;
            control._subscribed.Remove(oldSeries);
        }

        if (e.NewValue is INotifyCollectionChanged newSeries)
        {
            newSeries.CollectionChanged += control.OnCollectionChanged;
            control._subscribed.Add(newSeries);
        }

        control.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 2 || height <= 2)
        {
            return;
        }

        var color = Stroke is SolidColorBrush solid ? solid.Color : Color.FromRgb(0x4C, 0xAF, 0x50);
        var lineBrush = new SolidColorBrush(color);
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B)), 1);
        var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(0x90, color.R, color.G, color.B)), 1);
        var fillBrush = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B));
        var labelBrush = UiTheme.Brush("TextSecondaryBrush", Color.FromRgb(0xB0, 0xB0, 0xB0));

        var primary = Read(Values);
        var secondary = Read(SecondaryValues);
        var max = Maximum > 0 ? Maximum : NiceMaximum(primary.Concat(secondary));
        var capacity = Math.Max(2, Capacity);
        var step = width / (capacity - 1);

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
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
        DrawSeries(dc, primary, max, step, width, height, FillArea ? fillBrush : null, new Pen(lineBrush, lineWidth) { LineJoin = PenLineJoin.Round });
        DrawSeries(dc, secondary, max, step, width, height, null,
            new Pen(lineBrush, lineWidth - 0.2) { LineJoin = PenLineJoin.Round, DashStyle = new DashStyle([3, 2], 0) });
        dc.Pop();

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (ShowGrid)
        {
            dc.DrawRectangle(null, borderPen, new Rect(0.5, 0.5, width - 1, height - 1));
            if (!string.IsNullOrEmpty(Title))
            {
                dc.DrawText(Format(Title, labelBrush, dpi), new Point(4, 2));
            }

            if (primary.Count > 0 || secondary.Count > 0)
            {
                var scale = Format(ScaleText(max), labelBrush, dpi);
                dc.DrawText(scale, new Point(width - scale.Width - 4, 2));
            }
        }

        if (!string.IsNullOrEmpty(ValueLabel))
        {
            DrawValueBadge(dc, ValueLabel, dpi);
        }
    }

    private void DrawValueBadge(DrawingContext dc, string text, double dpi)
    {
        var label = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            10.5, UiTheme.Brush("TextPrimaryBrush", Colors.White), dpi);
        var background = UiTheme.Brush("SurfaceDarkBrush", Color.FromRgb(0x1E, 0x1E, 0x1E)).Clone();
        background.Opacity = 0.85;
        var box = new Rect(1, 1, label.Width + 8, label.Height + 2);
        dc.DrawRoundedRectangle(background, null, box, 3, 3);
        dc.DrawText(label, new Point(box.X + 4, box.Y + 1));
    }

    private static void DrawSeries(DrawingContext dc, IReadOnlyList<double> values, double max, double step,
        double width, double height, Brush? fill, Pen pen)
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
                ctx.BeginFigure(new Point(points[0].X, height), true, true);
                foreach (var point in points)
                {
                    ctx.LineTo(point, false, true);
                }

                ctx.LineTo(new Point(points[^1].X, height), false, false);
            }

            area.Freeze();
            dc.DrawGeometry(fill, null, area);
        }

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            for (var i = 1; i < points.Length; i++)
            {
                ctx.LineTo(points[i], true, true);
            }
        }

        line.Freeze();
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

    private static FormattedText Format(string text, Brush brush, double dpi) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, brush, dpi);
}
