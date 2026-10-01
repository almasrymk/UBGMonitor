using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace MonitorAgent.Desktop.Controls;

/// <summary>
/// Resize grips laid over a dashboard panel: right edge changes width, bottom edge changes height, corner changes both.
/// Also draws the selection outline (multi-select on the dashboard).
/// </summary>
public sealed class PanelResizeGrips : Panel
{
    public const double MinPanelHeight = 80;
    private const double EdgeThickness = 6;
    private const double CornerSize = 14;

    private readonly Control _panel;
    private readonly DashboardBoard _board;
    private readonly Action _changed;
    private Size _start;
    private Point _startMouse;
    private readonly Border _outline = new()
    {
        BorderThickness = new Thickness(2),
        CornerRadius = new CornerRadius(8),
        IsHitTestVisible = false,
        IsVisible = false,
        [!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentGreenBrush")
    };

    public PanelResizeGrips(Control panel, DashboardBoard board, Action changed)
    {
        _panel = panel;
        _board = board;
        _changed = changed;
        Children.Add(_outline);
        Children.Add(Grip(StandardCursorType.SizeWestEast, resizeWidth: true, resizeHeight: false,
            HorizontalAlignment.Right, VerticalAlignment.Stretch, EdgeThickness, double.NaN, new Thickness(0, 0, 0, CornerSize)));
        Children.Add(Grip(StandardCursorType.SizeNorthSouth, resizeWidth: false, resizeHeight: true,
            HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, EdgeThickness, new Thickness(0, 0, CornerSize, 0)));
        var corner = Grip(StandardCursorType.BottomRightCorner, resizeWidth: true, resizeHeight: true,
            HorizontalAlignment.Right, VerticalAlignment.Bottom, CornerSize, CornerSize, new Thickness(0, 0, 2, 2));
        corner.Child = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 12,4 L 4,12 M 12,8 L 8,12"),
            StrokeThickness = 1.2,
            [!Shape.StrokeProperty] = new DynamicResourceExtension("TextSecondaryBrush")
        };
        Children.Add(corner);
    }

    public bool IsSelected
    {
        get => _outline.IsVisible;
        set => _outline.IsVisible = value;
    }

    private Border Grip(StandardCursorType cursor, bool resizeWidth, bool resizeHeight,
        HorizontalAlignment horizontal, VerticalAlignment vertical, double width, double height, Thickness margin)
    {
        var grip = new Border
        {
            Background = Brushes.Transparent,
            Cursor = new Cursor(cursor),
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            Width = width,
            Height = height,
            Margin = margin
        };

        var dragging = false;
        grip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed || e.ClickCount > 1)
            {
                return;
            }

            dragging = true;
            _start = _panel.Bounds.Size;
            _startMouse = e.GetPosition(_board);
            e.Pointer.Capture(grip);
            e.Handled = true;
        };
        grip.PointerMoved += (_, e) =>
        {
            if (dragging)
            {
                Resize(e.GetPosition(_board) - _startMouse, resizeWidth, resizeHeight);
            }
        };
        grip.PointerReleased += (_, e) =>
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;
            e.Pointer.Capture(null);
            _changed();
            e.Handled = true;
        };
        grip.DoubleTapped += (_, e) =>
        {
            if (resizeHeight)
            {
                _panel.MinHeight = 0;
            }

            _changed();
            e.Handled = true;
        };
        return grip;
    }

    private void Resize(Vector moved, bool resizeWidth, bool resizeHeight)
    {
        if (resizeWidth)
        {
            var span = _board.Span(_board.Bounds.Width);
            if (span > 0)
            {
                var left = DashboardBoard.GetLeft(_panel);
                var ratio = DashboardBoard.SnapRatio((_start.Width + moved.X + DashboardBoard.Gap) / span);
                ratio = Math.Clamp(ratio, DashboardBoard.MinWidthRatio, Math.Max(DashboardBoard.MinWidthRatio, 1 - left));
                if (Math.Abs(ratio - DashboardBoard.GetWidthRatio(_panel)) > 0.0001)
                {
                    DashboardBoard.SetWidthRatio(_panel, ratio);
                }
            }
        }

        // The dragged height is a minimum: a panel still grows when its content does (e.g. an expanded section).
        if (resizeHeight)
        {
            var height = Math.Max(MinPanelHeight, _start.Height + moved.Y);
            _panel.Height = double.NaN;
            _panel.MinHeight = Math.Round(height / DashboardBoard.SnapStep) * DashboardBoard.SnapStep;
        }
    }
}
