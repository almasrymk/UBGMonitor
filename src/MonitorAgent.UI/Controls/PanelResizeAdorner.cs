using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace MonitorAgent.UI.Controls;

/// <summary>
/// Resize grips for a dashboard panel: right edge changes width, bottom edge changes height, corner changes both.
/// </summary>
public sealed class PanelResizeAdorner : Adorner
{
    public const double MinPanelHeight = 80;
    private const double EdgeThickness = 6;

    private readonly VisualCollection _visuals;
    private readonly Thumb _corner;
    private readonly Thumb _right;
    private readonly Thumb _bottom;
    private readonly FrameworkElement _panel;
    private readonly DashboardBoard _board;
    private readonly Action _changed;
    private Size _start;
    private Point _startMouse;

    private bool _isSelected;

    /// <summary>Draws a selection outline around the panel (multi-select on the dashboard).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                InvalidateVisual();
            }
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (!_isSelected)
        {
            return;
        }

        var accent = TryFindResource("AccentGreenBrush") as Brush ?? Brushes.LimeGreen;
        var rect = new Rect(AdornedElement.RenderSize);
        rect.Inflate(-1, -1);
        dc.DrawRoundedRectangle(null, new Pen(accent, 2), rect, 8, 8);
    }

    public PanelResizeAdorner(FrameworkElement panel, DashboardBoard board, Style? cornerStyle, Action changed)
        : base(panel)
    {
        _panel = panel;
        _board = board;
        _changed = changed;
        _right = CreateGrip(EdgeGrip(Cursors.SizeWE), resizeWidth: true, resizeHeight: false);
        _bottom = CreateGrip(EdgeGrip(Cursors.SizeNS), resizeWidth: false, resizeHeight: true);
        _corner = CreateGrip(cornerStyle, resizeWidth: true, resizeHeight: true);
        _visuals = new VisualCollection(this) { _right, _bottom, _corner };
    }

    private static Style EdgeGrip(Cursor cursor)
    {
        var template = new ControlTemplate(typeof(Thumb))
        {
            VisualTree = new FrameworkElementFactory(typeof(Border))
        };
        template.VisualTree.SetValue(Border.BackgroundProperty, Brushes.Transparent);

        var style = new Style(typeof(Thumb));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(CursorProperty, cursor));
        return style;
    }

    private Thumb CreateGrip(Style? style, bool resizeWidth, bool resizeHeight)
    {
        var grip = new Thumb { Style = style };
        grip.DragStarted += (_, _) =>
        {
            _start = new Size(_panel.ActualWidth, _panel.ActualHeight);
            _startMouse = Mouse.GetPosition(_board);
        };
        grip.DragDelta += (_, _) => Resize(resizeWidth, resizeHeight);
        grip.DragCompleted += (_, _) => _changed();
        grip.MouseDoubleClick += (_, e) =>
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

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override Size MeasureOverride(Size constraint)
    {
        foreach (Visual visual in _visuals)
        {
            ((UIElement)visual).Measure(constraint);
        }

        return AdornedElement.RenderSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = AdornedElement.RenderSize;
        var corner = _corner.DesiredSize;
        _corner.Arrange(new Rect(
            Math.Max(0, size.Width - corner.Width - 2),
            Math.Max(0, size.Height - corner.Height - 2),
            corner.Width,
            corner.Height));
        _right.Arrange(new Rect(
            Math.Max(0, size.Width - EdgeThickness), 0,
            EdgeThickness, Math.Max(0, size.Height - corner.Height)));
        _bottom.Arrange(new Rect(
            0, Math.Max(0, size.Height - EdgeThickness),
            Math.Max(0, size.Width - corner.Width), EdgeThickness));
        return finalSize;
    }

    private void Resize(bool resizeWidth, bool resizeHeight)
    {
        var moved = Mouse.GetPosition(_board) - _startMouse;

        if (resizeWidth)
        {
            var span = _board.Span(_board.ActualWidth);
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
