using Avalonia;
using Avalonia.Media;
using MonitorAgent.UI.Services;

namespace MonitorAgent.Desktop.Controls;

public sealed class CircularProgressControl : ThemedControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<CircularProgressControl, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> ProgressBrushProperty =
        AvaloniaProperty.Register<CircularProgressControl, IBrush?>(nameof(ProgressBrush), Brushes.LimeGreen);

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<CircularProgressControl, double>(nameof(Thickness), 10d);

    static CircularProgressControl()
    {
        AffectsRender<CircularProgressControl>(ValueProperty, ProgressBrushProperty, ThicknessProperty);
    }

    public CircularProgressControl()
    {
        Width = 120;
        Height = 120;
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush? ProgressBrush { get => GetValue(ProgressBrushProperty); set => SetValue(ProgressBrushProperty, value); }
    public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    public override void Render(DrawingContext dc)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
        {
            return;
        }

        var thickness = Thickness;
        var radius = (size / 2) - (thickness / 2) - 2;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var track = new Pen(UiTheme.Brush("BorderColorBrush", Color.FromRgb(0x3E, 0x3E, 0x42)), thickness, lineCap: PenLineCap.Round);
        dc.DrawEllipse(null, track, center, radius, radius);

        var clamped = Math.Clamp(Value, 0, 100);
        if (clamped > 0.1)
        {
            var progress = new Pen(ProgressBrush ?? Brushes.LimeGreen, thickness, lineCap: PenLineCap.Round);
            dc.DrawGeometry(null, progress, Arc(center, radius, -90, clamped / 100d * 359.9));
        }

        var label = Format($"{clamped:0}%", 22, UiTheme.Brush("TextPrimaryBrush", Colors.White), bold: true);
        dc.DrawText(label, new Point(center.X - label.Width / 2, center.Y - label.Height / 2));
    }
}
