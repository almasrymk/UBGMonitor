using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MonitorAgent.UI.Services;

namespace MonitorAgent.Desktop.Controls;

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

    public static readonly AttachedProperty<double> LeftProperty =
        AvaloniaProperty.RegisterAttached<DashboardBoard, Control, double>("Left");

    public static readonly AttachedProperty<double> WidthRatioProperty =
        AvaloniaProperty.RegisterAttached<DashboardBoard, Control, double>("WidthRatio", 0.25);

    public static readonly AttachedProperty<double> TopProperty =
        AvaloniaProperty.RegisterAttached<DashboardBoard, Control, double>("Top");

    static DashboardBoard()
    {
        AffectsParentMeasure<DashboardBoard>(LeftProperty, WidthRatioProperty, TopProperty);
    }

    public static double GetLeft(Control element) => element.GetValue(LeftProperty);
    public static void SetLeft(Control element, double value) => element.SetValue(LeftProperty, value);
    public static double GetWidthRatio(Control element) => element.GetValue(WidthRatioProperty);
    public static void SetWidthRatio(Control element, double value) => element.SetValue(WidthRatioProperty, value);
    public static double GetTop(Control element) => element.GetValue(TopProperty);
    public static void SetTop(Control element, double value) => element.SetValue(TopProperty, value);

    private readonly Dictionary<Control, Rect> _placed = new();
    private readonly PreviewLayer _previewLayer;
    private IReadOnlyList<Rect> _preview = [];
    private bool _extraDropRoom;

    public DashboardBoard()
    {
        _previewLayer = new PreviewLayer(this) { IsHitTestVisible = false, ZIndex = 1000 };
        Children.Add(_previewLayer);
    }

    /// <summary>The dashboard panels (every child except the preview layer).</summary>
    private IEnumerable<Control> Panels => Children.Where(child => child != _previewLayer);

    public static double SnapRatio(double ratio) => Math.Round(ratio * HorizontalSteps) / HorizontalSteps;

    /// <summary>Board width plus one gap, so a ratio of 1 spans the whole board with no trailing gap.</summary>
    public double Span(double width) => Math.Max(0, width) + Gap;

    public Rect? PlacedRect(Control child) => _placed.TryGetValue(child, out var rect) ? rect : null;

    private static (double Left, double Width) Horizontal(Control child)
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

    public (double Left, double Top) Snap(Control child, Point topLeft)
    {
        var (_, width) = Horizontal(child);
        var span = Span(Bounds.Width);
        var left = span <= 0 ? 0 : SnapRatio(topLeft.X / span);
        left = Math.Clamp(left, 0, 1 - width);
        var top = Math.Max(0, Math.Round(topLeft.Y / SnapStep) * SnapStep);
        return (left, top);
    }

    public void ShowPreview(Control child, double left, double top)
    {
        var (_, width) = Horizontal(child);
        var height = PlacedRect(child)?.Height ?? child.DesiredSize.Height;
        ShowPreview(RectFor(Bounds.Width, left, width, top, height));
    }

    public void ShowPreview(Rect rect) => ShowPreview([rect]);

    public void ShowPreview(IReadOnlyList<Rect> rects)
    {
        _preview = rects;
        SetDropRoom(true);
        _previewLayer.InvalidateVisual();
    }

    public void ClearPreview()
    {
        _preview = [];
        SetDropRoom(false);
        _previewLayer.InvalidateVisual();
    }

    /// <summary>Pins every panel to where it is shown now, so later moves don't make others jump.</summary>
    public void FreezePositions()
    {
        foreach (var child in Panels)
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

        _previewLayer.Measure(availableSize);
        var ordered = Panels
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
            if (!child.IsVisible)
            {
                continue;
            }

            rect = rect.WithHeight(child.DesiredSize.Height);
            rect = rect.WithY(FirstFreeY(rect));
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
        var y = rect.Y;
        var moved = true;
        while (moved)
        {
            moved = false;
            foreach (var other in _placed.Values)
            {
                var sharesColumns = rect.Left < other.Right - 0.5 && other.Left < rect.Right - 0.5;
                var overlaps = y < other.Bottom + Gap && other.Top < y + rect.Height + Gap;
                if (sharesColumns && overlaps)
                {
                    y = other.Bottom + Gap;
                    moved = true;
                }
            }
        }

        return y;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _previewLayer.Arrange(new Rect(finalSize));
        foreach (var child in Panels)
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

    /// <summary>Draws the drop preview over the panels (a panel cannot draw on itself in Avalonia).</summary>
    private sealed class PreviewLayer(DashboardBoard board) : Control
    {
        public override void Render(DrawingContext dc)
        {
            if (board._preview.Count == 0)
            {
                return;
            }

            var accent = UiTheme.Brush("AccentGreenBrush", Colors.LimeGreen);
            var color = accent is ISolidColorBrush solid ? solid.Color : Colors.LimeGreen;
            var fill = new SolidColorBrush(Color.FromArgb(0x1F, color.R, color.G, color.B));
            var pen = new Pen(accent, 2, DashStyle.Dash);
            foreach (var preview in board._preview)
            {
                dc.DrawRectangle(fill, pen, preview, 8, 8);
            }
        }
    }
}