using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace MonitorAgent.Desktop.Services;

/// <summary>
/// Pin / auto-hide behaviour for a docked panel. When unpinned the panel collapses to a thin strip,
/// slides out over the content while the mouse is on the strip or the panel, and slides back after it leaves.
/// </summary>
public sealed class AutoHidePanel
{
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(350);

    private readonly Control _panel;
    private readonly Control _strip;
    private readonly TranslateTransform _shift;
    private readonly bool _horizontal;
    private readonly Action<bool> _applyDock;
    private readonly Window _window;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _slideTimer;
    private bool _open;
    private DateTime _outsideSince = DateTime.MaxValue;
    private Point? _pointer;
    private double _slideFrom;
    private double _slideTo;
    private DateTime _slideStart;
    private Action? _slideDone;

    public AutoHidePanel(Control panel, Control strip, TranslateTransform shift, bool horizontal, Action<bool> applyDock, Window window)
    {
        _panel = panel;
        _strip = strip;
        _shift = shift;
        _horizontal = horizontal;
        _applyDock = applyDock;
        _window = window;
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _pollTimer.Tick += (_, _) => Poll();
        _slideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        _slideTimer.Tick += (_, _) => SlideStep();

        // The window reports where the pointer is (also over handled children); leaving the window counts as outside.
        window.AddHandler(InputElement.PointerMovedEvent, (_, e) => _pointer = e.GetPosition(window),
            RoutingStrategies.Tunnel, handledEventsToo: true);
        window.PointerExited += (_, _) => _pointer = null;
        window.Deactivated += (_, _) => _pointer = null;
    }

    public bool Pinned { get; private set; } = true;

    private double Offset
    {
        get => _horizontal ? _shift.X : _shift.Y;
        set
        {
            if (_horizontal)
            {
                _shift.X = value;
            }
            else
            {
                _shift.Y = value;
            }
        }
    }

    private void Poll()
    {
        if (Pinned)
        {
            _pollTimer.Stop();
            return;
        }

        var overStrip = IsPointerOver(_strip);
        var overPanel = _open && IsPointerOver(_panel);
        if (!_open)
        {
            if (overStrip)
            {
                Open();
            }

            return;
        }

        if (overStrip || overPanel || HasOpenPopup())
        {
            _outsideSince = DateTime.MaxValue;
            return;
        }

        if (_outsideSince == DateTime.MaxValue)
        {
            _outsideSince = DateTime.UtcNow;
        }
        else if (DateTime.UtcNow - _outsideSince >= CloseDelay)
        {
            Close();
        }
    }

    private bool IsPointerOver(Control element)
    {
        if (!element.IsVisible || _pointer is not { } pointer || _window.TranslatePoint(pointer, element) is not { } point)
        {
            return false;
        }

        return point.X >= 0 && point.Y >= 0 && point.X <= element.Bounds.Width && point.Y <= element.Bounds.Height;
    }

    public void SetPinned(bool pinned)
    {
        Pinned = pinned;
        _slideTimer.Stop();
        _shift.X = 0;
        _shift.Y = 0;
        _applyDock(pinned);
        _strip.IsVisible = !pinned;
        Show(true);
        _open = true;
        _outsideSince = DateTime.MaxValue;

        if (pinned)
        {
            _pollTimer.Stop();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (Pinned)
            {
                return;
            }

            // When unpinned from inside the panel it stays open until the mouse leaves.
            if (!IsPointerOver(_panel))
            {
                Offset = HiddenOffset();
                Show(false);
                _open = false;
            }

            _pollTimer.Start();
        }, DispatcherPriority.Loaded);
    }

    public void Close()
    {
        if (Pinned || !_open)
        {
            return;
        }

        _open = false;
        Slide(HiddenOffset(), () =>
        {
            if (!_open)
            {
                Show(false);
            }
        });
    }

    private void Open()
    {
        if (Pinned || _open)
        {
            return;
        }

        _open = true;
        _outsideSince = DateTime.MaxValue;
        Show(true);
        Slide(0, null);
    }

    private void Show(bool visible)
    {
        _panel.Opacity = visible ? 1 : 0;
        _panel.IsHitTestVisible = visible;
    }

    private double HiddenOffset()
    {
        var margin = _panel.Margin;
        return _horizontal
            ? -(_panel.Bounds.Width + margin.Left + margin.Right)
            : -(_panel.Bounds.Height + margin.Top + margin.Bottom);
    }

    private void Slide(double to, Action? completed)
    {
        _slideFrom = Offset;
        _slideTo = to;
        _slideStart = DateTime.UtcNow;
        _slideDone = completed;
        _slideTimer.Start();
    }

    private void SlideStep()
    {
        var t = Math.Clamp((DateTime.UtcNow - _slideStart) / SlideDuration, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);
        Offset = _slideFrom + (_slideTo - _slideFrom) * eased;
        if (t >= 1)
        {
            _slideTimer.Stop();
            var done = _slideDone;
            _slideDone = null;
            done?.Invoke();
        }
    }

    private bool HasOpenPopup() => _panel.GetVisualDescendants().OfType<ComboBox>().Any(combo => combo.IsDropDownOpen);
}
