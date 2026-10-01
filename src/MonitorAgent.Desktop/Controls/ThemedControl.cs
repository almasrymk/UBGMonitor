using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MonitorAgent.UI.Services;

namespace MonitorAgent.Desktop.Controls;

/// <summary>A drawn control that redraws when the theme changes.</summary>
public abstract class ThemedControl : Control
{
    protected static readonly Typeface Face = new(FontFamily.Default);
    protected static readonly Typeface BoldFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    private void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UiTheme.Changed += OnThemeChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UiTheme.Changed -= OnThemeChanged;
        base.OnDetachedFromVisualTree(e);
    }

    protected static FormattedText Format(string text, double size, IBrush brush, bool bold = false)
        => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? BoldFace : Face, size, brush);

    protected static Point Polar(Point center, double radius, double angleDegrees)
    {
        var rad = angleDegrees * Math.PI / 180d;
        return new Point(center.X + (radius * Math.Cos(rad)), center.Y + (radius * Math.Sin(rad)));
    }

    protected static Geometry Arc(Point center, double radius, double startDeg, double sweepDeg)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(Polar(center, radius, startDeg), false);
        context.ArcTo(Polar(center, radius, startDeg + sweepDeg), new Size(radius, radius), 0, sweepDeg > 180, SweepDirection.Clockwise);
        context.EndFigure(false);
        return geometry;
    }

    protected static Color ColorOf(IBrush? brush, Color fallback) => brush is ISolidColorBrush solid ? solid.Color : fallback;

    protected static IBrush WithAlpha(Color color, byte alpha) => new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
}
