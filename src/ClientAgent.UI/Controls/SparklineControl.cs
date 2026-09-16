using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace ClientAgent.UI.Controls;

public sealed class SparklineControl : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IEnumerable), typeof(SparklineControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(SparklineControl),
        new FrameworkPropertyMetadata(Brushes.LimeGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    private INotifyCollectionChanged? _subscribed;

    public IEnumerable? Values
    {
        get => (IEnumerable?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SparklineControl)d;
        if (control._subscribed is not null)
        {
            control._subscribed.CollectionChanged -= control.OnCollectionChanged;
        }

        control._subscribed = e.NewValue as INotifyCollectionChanged;
        if (control._subscribed is not null)
        {
            control._subscribed.CollectionChanged += control.OnCollectionChanged;
        }

        control.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var points = Values?.Cast<object>().Select(v => Convert.ToDouble(v)).ToList() ?? [];
        if (points.Count < 2 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var min = points.Min();
        var max = points.Max();
        var range = Math.Max(1, max - min);
        var stepX = ActualWidth / (points.Count - 1);
        var mapped = points.Select((v, i) => new Point(
            i * stepX,
            ActualHeight - ((v - min) / range * ActualHeight))).ToList();

        var fill = new StreamGeometry();
        using (var ctx = fill.Open())
        {
            ctx.BeginFigure(new Point(mapped[0].X, ActualHeight), true, true);
            foreach (var point in mapped)
            {
                ctx.LineTo(point, true, true);
            }

            ctx.LineTo(new Point(mapped[^1].X, ActualHeight), true, false);
        }

        Brush fillBrush = Stroke is SolidColorBrush solid
            ? new SolidColorBrush(solid.Color) { Opacity = 0.22 }
            : new SolidColorBrush(Color.FromArgb(0x38, 0x21, 0x96, 0xF3));
        drawingContext.DrawGeometry(fillBrush, null, fill);

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(mapped[0], false, false);
            for (var i = 1; i < mapped.Count; i++)
            {
                ctx.LineTo(mapped[i], true, true);
            }
        }

        drawingContext.DrawGeometry(null, new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round }, line);
    }
}
