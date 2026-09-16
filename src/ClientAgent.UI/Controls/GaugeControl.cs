using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ClientAgent.UI.Controls;

public sealed class GaugeControl : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(GaugeControl),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(GaugeControl),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(GaugeControl),
        new FrameworkPropertyMetadata("Mbps", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(GaugeControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty NeedleBrushProperty = DependencyProperty.Register(
        nameof(NeedleBrush), typeof(Brush), typeof(GaugeControl),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public Brush NeedleBrush
    {
        get => (Brush)GetValue(NeedleBrushProperty);
        set => SetValue(NeedleBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 168 : Math.Max(140, availableSize.Width);
        var height = double.IsInfinity(availableSize.Height) ? 132 : Math.Max(110, availableSize.Height);
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var max = Maximum <= 0 ? 100 : Maximum;
        var ratio = Math.Clamp(Value / max, 0, 1);
        var center = new Point(ActualWidth / 2, ActualHeight * 0.78);
        var radius = Math.Min(ActualWidth / 2, ActualHeight * 0.72) - 10;
        var trackBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x52));
        var tickBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));
        var labelBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));

        dc.DrawGeometry(null, new Pen(trackBrush, 10) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            BuildArc(center, radius, 180, 180));

        for (var i = 0; i <= 10; i++)
        {
            var isMajor = i % 2 == 0;
            var angle = 180 + (i * 18);
            var outer = Polar(center, radius + 2, angle);
            var inner = Polar(center, radius - (isMajor ? 12 : 7), angle);
            dc.DrawLine(new Pen(tickBrush, isMajor ? 1.6 : 1), inner, outer);

            if (!isMajor)
            {
                continue;
            }

            var labelValue = Maximum * i / 10d;
            var label = Maximum <= 1
                ? labelValue.ToString("0.0", CultureInfo.InvariantCulture)
                : Maximum <= 10
                    ? labelValue.ToString("0.#", CultureInfo.InvariantCulture)
                    : labelValue.ToString("0", CultureInfo.InvariantCulture);
            var ft = Format(label, 10, labelBrush, dpi);
            var labelPoint = Polar(center, radius + 14, angle);
            var x = Math.Clamp(labelPoint.X - (ft.Width / 2), 0, Math.Max(0, ActualWidth - ft.Width));
            var y = Math.Clamp(labelPoint.Y - (ft.Height / 2), 0, Math.Max(0, ActualHeight - ft.Height));
            dc.DrawText(ft, new Point(x, y));
        }

        var needleAngle = 180 + (ratio * 180);
        var needleEnd = Polar(center, radius - 18, needleAngle);
        dc.DrawLine(new Pen(NeedleBrush, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Triangle }, center, needleEnd);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)), new Pen(tickBrush, 1.2), center, 6, 6);

        var valueText = string.Concat(Value.ToString("0.0", CultureInfo.InvariantCulture), " ", Unit);
        var valueFt = Format(valueText, 13, Brushes.White, dpi, FontWeights.SemiBold);
        dc.DrawText(valueFt, new Point(center.X - (valueFt.Width / 2), center.Y - valueFt.Height - 14));

        if (!string.IsNullOrWhiteSpace(Subtitle))
        {
            var subFt = Format(Subtitle, 11, labelBrush, dpi);
            dc.DrawText(subFt, new Point(center.X - (subFt.Width / 2), ActualHeight - subFt.Height - 2));
        }
    }

    private static Geometry BuildArc(Point center, double radius, double startDeg, double sweepDeg)
    {
        var start = Polar(center, radius, startDeg);
        var end = Polar(center, radius, startDeg + sweepDeg);
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            IsLargeArc = sweepDeg > 180,
            SweepDirection = SweepDirection.Clockwise,
            IsStroked = true
        });
        return new PathGeometry([figure]);
    }

    private static Point Polar(Point center, double radius, double angleDegrees)
    {
        var rad = angleDegrees * Math.PI / 180d;
        return new Point(center.X + (radius * Math.Cos(rad)), center.Y + (radius * Math.Sin(rad)));
    }

    private static FormattedText Format(string text, double size, Brush brush, double dpi, FontWeight? weight = null)
        => new(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
            size,
            brush,
            dpi);
}
