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
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (var i = 0; i < points.Count; i++)
            {
                var x = i * stepX;
                var y = ActualHeight - ((points[i] - min) / range * ActualHeight);
                if (i == 0)
                {
                    ctx.BeginFigure(new Point(x, y), false, false);
                }
                else
                {
                    ctx.LineTo(new Point(x, y), true, true);
                }
            }
        }

        drawingContext.DrawGeometry(null, new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round }, geo);
    }
}
