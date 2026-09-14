using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ClientAgent.UI.Controls;

public sealed class CircularProgressControl : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(CircularProgressControl),
        new PropertyMetadata(0d, OnChanged));

    public static readonly DependencyProperty ProgressBrushProperty = DependencyProperty.Register(
        nameof(ProgressBrush), typeof(Brush), typeof(CircularProgressControl),
        new PropertyMetadata(Brushes.LimeGreen, OnChanged));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(CircularProgressControl),
        new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)), OnChanged));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(CircularProgressControl),
        new PropertyMetadata(10d, OnChanged));

    private readonly Path _track = new() { StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Fill = Brushes.Transparent };
    private readonly Path _progress = new() { StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Fill = Brushes.Transparent };
    private readonly System.Windows.Controls.TextBlock _label = new()
    {
        Foreground = Brushes.White,
        FontWeight = FontWeights.Bold,
        FontSize = 22,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontFamily = new FontFamily("Segoe UI")
    };

    public CircularProgressControl()
    {
        var grid = new System.Windows.Controls.Grid();
        grid.Children.Add(_track);
        grid.Children.Add(_progress);
        grid.Children.Add(_label);
        Content = grid;
        Width = 120;
        Height = 120;
        Loaded += (_, _) => Rebuild();
        SizeChanged += (_, _) => Rebuild();
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush ProgressBrush
    {
        get => (Brush)GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((CircularProgressControl)d).Rebuild();

    private void Rebuild()
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            size = 120;
        }

        var thickness = Thickness;
        var radius = (size / 2) - (thickness / 2) - 2;
        var center = new Point(size / 2, size / 2);
        _track.Stroke = TrackBrush;
        _track.StrokeThickness = thickness;
        _progress.Stroke = ProgressBrush;
        _progress.StrokeThickness = thickness;
        _track.Data = BuildArc(center, radius, 359.9);
        var clamped = Math.Clamp(Value, 0, 100);
        _progress.Data = clamped <= 0.1 ? Geometry.Empty : BuildArc(center, radius, clamped / 100d * 359.9);
        _label.Text = $"{clamped:0}%";
    }

    private static Geometry BuildArc(Point center, double radius, double angle)
    {
        var start = Polar(center, radius, -90);
        var end = Polar(center, radius, -90 + angle);
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            IsLargeArc = angle > 180,
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
}
