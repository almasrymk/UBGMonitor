using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClientAgent.UI.Controls;

/// <summary>
/// Free-placement dashboard: panel position and width are fractions of the board width (so the layout
/// follows window resizing), snapped to a fine grid. A panel that would overlap another is pushed below it.
/// </summary>
public class DashboardBoard : Panel
{
    public const double Gap = 10;
    public const double SnapStep = 10;
    public const int HorizontalSteps = 24;
    public const double MinWidthRatio = 2d / HorizontalSteps;

    public static readonly DependencyProperty LeftProperty = DependencyProperty.RegisterAttached(
        "Left", typeof(double), typeof(DashboardBoard),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static readonly DependencyProperty WidthRatioProperty = DependencyProperty.RegisterAttached(
        "WidthRatio", typeof(double), typeof(DashboardBoard),
        new FrameworkPropertyMetadata(0.25, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static readonly DependencyProperty TopProperty = DependencyProperty.RegisterAttached(
        "Top", typeof(double), typeof(DashboardBoard),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static double GetLeft(UIElement element) => (double)element.GetValue(LeftProperty);
    public static void SetLeft(UIElement element, double value) => element.SetValue(LeftProperty, value);
    public static double GetWidthRatio(UIElement element) => (double)element.GetValue(WidthRatioProperty);
    public static void SetWidthRatio(UIElement element, double value) => element.SetValue(WidthRatioProperty, value);
    public static double GetTop(UIElement element) => (double)element.GetValue(TopProperty);
    public static void SetTop(UIElement element, double value) => element.SetValue(TopProperty, value);

    private readonly Dictionary<UIElement, Rect> _placed = new();
    private IReadOnlyList<Rect> _preview = [];
    private bool _extraDropRoom;

    public static double SnapRatio(double ratio) => Math.Round(ratio * HorizontalSteps) / HorizontalSteps;

    /// <summary>Board width plus one gap, so a ratio of 1 spans the whole board with no trailing gap.</summary>
    public double Span(double width) => Math.Max(0, width) + Gap;

    public Rect PlacedRect(UIElement child) => _placed.TryGetValue(child, out var rect) ? rect : Rect.Empty;

    private static (double Left, double Width) Horizontal(UIElement child)
    {
        var width = Math.Clamp(GetWidthRatio(child), MinWidthRatio, 1);
        var left = Math.Clamp(GetLeft(child), 0, 1 - width);
        return (left, width);
    }

    private Rect RectFor(double boardWidth, double left, double widthRatio, double top, double height)
    {
        var span = Span(boardWidth);
        return new Rect(left * span, top, Math.Max(0, widthRatio * span - Gap), height);
    }

    public (double Left, double Top) Snap(UIElement child, Point topLeft)
    {
        var (_, width) = Horizontal(child);
        var span = Span(ActualWidth);
        var left = span <= 0 ? 0 : SnapRatio(topLeft.X / span);
        left = Math.Clamp(left, 0, 1 - width);
        var top = Math.Max(0, Math.Round(topLeft.Y / SnapStep) * SnapStep);
        return (left, top);
    }

    public void ShowPreview(UIElement child, double left, double top)
    {
        var (_, width) = Horizontal(child);
        var height = PlacedRect(child) is { IsEmpty: false } rect ? rect.Height : child.DesiredSize.Height;
        ShowPreview(RectFor(ActualWidth, left, width, top, height));
    }

    public void ShowPreview(Rect rect) => ShowPreview([rect]);

    public void ShowPreview(IReadOnlyList<Rect> rects)
    {
        _preview = rects;
        SetDropRoom(true);
        InvalidateVisual();
    }

    public void ClearPreview()
    {
        _preview = [];
        SetDropRoom(false);
        InvalidateVisual();
    }

    /// <summary>Pins every panel to where it is shown now, so later moves don't make others jump.</summary>
    public void FreezePositions()
    {
        foreach (UIElement child in InternalChildren)
        {
            if (_placed.TryGetValue(child, out var rect))
            {
                SetTop(child, rect.Top);
            }
        }
    }

    private void SetDropRoom(bool value)
    {
        if (_extraDropRoom != value)
        {
            _extraDropRoom = value;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 1200 : availableSize.Width;
        _placed.Clear();

        var ordered = InternalChildren.Cast<UIElement>()
            .Select((child, index) => (child, index))
            .OrderBy(item => GetTop(item.child))
            .ThenBy(item => item.index)
            .Select(item => item.child);

        double bottom = 0;
        foreach (var child in ordered)
        {
            var (left, widthRatio) = Horizontal(child);
            var rect = RectFor(width, left, widthRatio, Math.Max(0, GetTop(child)), 0);
            child.Measure(new Size(rect.Width, double.PositiveInfinity));

            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            rect.Height = child.DesiredSize.Height;
            rect.Y = FirstFreeY(rect);
            _placed[child] = rect;
            bottom = Math.Max(bottom, rect.Bottom);
        }

        if (_extraDropRoom)
        {
            bottom += 300;
        }

        return new Size(width, bottom);
    }

    private double FirstFreeY(Rect rect)
    {
        var moved = true;
        while (moved)
        {
            moved = false;
            foreach (var other in _placed.Values)
            {
                var sharesColumns = rect.Left < other.Right - 0.5 && other.Left < rect.Right - 0.5;
                var overlaps = rect.Top < other.Bottom + Gap && other.Top < rect.Bottom + Gap;
                if (sharesColumns && overlaps)
                {
                    rect.Y = other.Bottom + Gap;
                    moved = true;
                }
            }
        }

        return rect.Y;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            if (!_placed.TryGetValue(child, out var placed))
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            var (left, widthRatio) = Horizontal(child);
            var rect = RectFor(finalSize.Width, left, widthRatio, placed.Y, placed.Height);
            _placed[child] = rect;
            child.Arrange(rect);
        }

        return finalSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_preview.Count == 0)
        {
            return;
        }

        var accent = TryFindResource("AccentGreenBrush") as Brush ?? Brushes.LimeGreen;
        var fill = accent.Clone();
        fill.Opacity = 0.12;
        var pen = new Pen(accent, 2) { DashStyle = DashStyles.Dash };
        foreach (var preview in _preview)
        {
            dc.DrawRoundedRectangle(fill, pen, preview, 8, 8);
        }
    }
}
