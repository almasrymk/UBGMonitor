using System.Globalization;
using Avalonia;
using Avalonia.Media;
using MonitorAgent.UI.Services;

namespace MonitorAgent.Desktop.Controls;

public sealed class GaugeControl : ThemedControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<GaugeControl, double>(nameof(Value));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<GaugeControl, double>(nameof(Maximum), 100d);

    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<GaugeControl, string>(nameof(Unit), "Mbps");

    public static readonly StyledProperty<string> SubtitleProperty =
        AvaloniaProperty.Register<GaugeControl, string>(nameof(Subtitle), string.Empty);

    public static readonly StyledProperty<IBrush?> NeedleBrushProperty =
        AvaloniaProperty.Register<GaugeControl, IBrush?>(nameof(NeedleBrush), Brushes.White);

    static GaugeControl()
    {
        AffectsRender<GaugeControl>(ValueProperty, MaximumProperty, UnitProperty, SubtitleProperty, NeedleBrushProperty);
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public IBrush? NeedleBrush { get => GetValue(NeedleBrushProperty); set => SetValue(NeedleBrushProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 168 : Math.Max(140, availableSize.Width);
        var height = double.IsInfinity(availableSize.Height) ? 132 : Math.Max(110, availableSize.Height);
        return new Size(width, height);
    }

    public override void Render(DrawingContext dc)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var max = Maximum <= 0 ? 100 : Maximum;
        var ratio = Math.Clamp(Value / max, 0, 1);
        var center = new Point(width / 2, height * 0.78);
        var radius = Math.Min(width / 2, height * 0.72) - 10;
        var trackBrush = UiTheme.Brush("ButtonBrush", Color.FromRgb(0x4A, 0x4A, 0x52));
        var tickBrush = UiTheme.Brush("TextSecondaryBrush", Color.FromRgb(0xC8, 0xC8, 0xC8));
        var labelBrush = UiTheme.Brush("TextSecondaryBrush", Color.FromRgb(0xB0, 0xB0, 0xB0));
        var valueBrush = UiTheme.Brush("TextPrimaryBrush", Colors.White);
        var hubBrush = UiTheme.Brush("ChipFillBrush", Color.FromRgb(0x2A, 0x2A, 0x30));

        dc.DrawGeometry(null, new Pen(trackBrush, 10, lineCap: PenLineCap.Round), Arc(center, radius, 180, 180));

        for (var i = 0; i <= 10; i++)
        {
            var isMajor = i % 2 == 0;
            var angle = 180 + (i * 18);
            dc.DrawLine(new Pen(tickBrush, isMajor ? 1.6 : 1), Polar(center, radius - (isMajor ? 12 : 7), angle), Polar(center, radius + 2, angle));
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
            var ft = Format(label, 10, labelBrush);
            var labelPoint = Polar(center, radius + 14, angle);
            var x = Math.Clamp(labelPoint.X - (ft.Width / 2), 0, Math.Max(0, width - ft.Width));
            var y = Math.Clamp(labelPoint.Y - (ft.Height / 2), 0, Math.Max(0, height - ft.Height));
            dc.DrawText(ft, new Point(x, y));
        }

        var needleEnd = Polar(center, radius - 18, 180 + (ratio * 180));
        dc.DrawLine(new Pen(NeedleBrush ?? Brushes.White, 2.2, lineCap: PenLineCap.Round), center, needleEnd);
        dc.DrawEllipse(hubBrush, new Pen(tickBrush, 1.2), center, 6, 6);

        var valueFt = Format(string.Concat(Value.ToString("0.0", CultureInfo.InvariantCulture), " ", Unit), 13, valueBrush, bold: true);
        dc.DrawText(valueFt, new Point(center.X - (valueFt.Width / 2), center.Y - valueFt.Height - 14));

        if (!string.IsNullOrWhiteSpace(Subtitle))
        {
            var subFt = Format(Subtitle, 11, labelBrush);
            dc.DrawText(subFt, new Point(center.X - (subFt.Width / 2), height - subFt.Height - 2));
        }
    }
}
