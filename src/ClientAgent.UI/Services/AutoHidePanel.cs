using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ClientAgent.UI.Services;

/// <summary>
/// Pin / auto-hide behaviour for a docked panel. When unpinned the panel collapses to a thin strip,
/// slides out over the content while the mouse is on the strip or the panel, and slides back after it leaves.
/// </summary>
public sealed class AutoHidePanel
{
    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(160));

    private readonly FrameworkElement _panel;
    private readonly FrameworkElement _strip;
    private readonly TranslateTransform _shift;
    private readonly bool _horizontal;
    private readonly Action<bool> _applyDock;
    private bool _open;

    public AutoHidePanel(
        FrameworkElement panel,
        FrameworkElement strip,
        TranslateTransform shift,
        bool horizontal,
        Action<bool> applyDock)
    {
        _panel = panel;
        _strip = strip;
        _shift = shift;
        _horizontal = horizontal;
        _applyDock = applyDock;
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _pollTimer.Tick += (_, _) => Poll();
    }

    private static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(350);
    private readonly DispatcherTimer _pollTimer;
    private DateTime _outsideSince = DateTime.MaxValue;

    /// <summary>
    /// Mouse enter/leave events are unreliable while the panel slides under a still mouse, so the real cursor
    /// position is polled instead.
    /// </summary>
    private void Poll()
    {
        if (Pinned)
        {
            _pollTimer.Stop();
            return;
        }

        var overStrip = IsCursorOver(_strip);
        var overPanel = _open && IsCursorOver(_panel);
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

    private static bool IsCursorOver(FrameworkElement element)
    {
        if (!element.IsVisible || PresentationSource.FromVisual(element) is not { } source
            || !NativeMethods.GetCursorPos(out var cursor))
        {
            return false;
        }

        if (source is System.Windows.Interop.HwndSource hwnd)
        {
            var under = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(cursor), 2);
            if (under != hwnd.Handle)
            {
                return false;
            }
        }

        var point = element.PointFromScreen(new Point(cursor.X, cursor.Y));
        return point.X >= 0 && point.Y >= 0 && point.X <= element.ActualWidth && point.Y <= element.ActualHeight;
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    }

    public bool Pinned { get; private set; } = true;

    public void SetPinned(bool pinned)
    {
        Pinned = pinned;
        _shift.BeginAnimation(_horizontal ? TranslateTransform.XProperty : TranslateTransform.YProperty, null);
        _shift.X = 0;
        _shift.Y = 0;
        _applyDock(pinned);
        _strip.Visibility = pinned ? Visibility.Collapsed : Visibility.Visible;
        _panel.Visibility = Visibility.Visible;
        _open = true;
        _outsideSince = DateTime.MaxValue;

        if (pinned)
        {
            _pollTimer.Stop();
            return;
        }

        _panel.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (Pinned)
            {
                return;
            }

            // When unpinned from inside the panel it stays open until the mouse leaves.
            if (!IsCursorOver(_panel))
            {
                SetShift(HiddenOffset());
                _panel.Visibility = Visibility.Hidden;
                _open = false;
            }

            _pollTimer.Start();
        });
    }

    public void Close()
    {
        if (Pinned || !_open)
        {
            return;
        }

        _open = false;
        Animate(HiddenOffset(), () =>
        {
            if (!_open)
            {
                _panel.Visibility = Visibility.Hidden;
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
        _panel.Visibility = Visibility.Visible;
        Animate(0, null);
    }

    private double HiddenOffset()
    {
        var margin = _panel.Margin;
        return _horizontal
            ? -(_panel.ActualWidth + margin.Left + margin.Right)
            : -(_panel.ActualHeight + margin.Top + margin.Bottom);
    }

    private void SetShift(double value)
    {
        var property = _horizontal ? TranslateTransform.XProperty : TranslateTransform.YProperty;
        _shift.BeginAnimation(property, null);
        _shift.SetValue(property, value);
    }

    private void Animate(double to, Action? completed)
    {
        var animation = new DoubleAnimation(to, SlideDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        if (completed is not null)
        {
            animation.Completed += (_, _) => completed();
        }

        _shift.BeginAnimation(_horizontal ? TranslateTransform.XProperty : TranslateTransform.YProperty, animation);
    }

    private bool HasOpenPopup()
    {
        foreach (var combo in FindChildren<ComboBox>(_panel))
        {
            if (combo.IsDropDownOpen)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<T> FindChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindChildren<T>(child))
            {
                yield return nested;
            }
        }
    }
}
