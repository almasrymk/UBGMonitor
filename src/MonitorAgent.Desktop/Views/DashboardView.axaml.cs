using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MonitorAgent.Desktop.Controls;
using MonitorAgent.UI.Services;
using MonitorAgent.UI.ViewModels;

namespace MonitorAgent.Desktop.Views;

public partial class DashboardView : UserControl
{
    private const double DragThreshold = 4;

    private static readonly string LayoutPath = AppPaths.File("dashboard-layout.json");
    private static readonly string MyDefaultPath = AppPaths.File("dashboard-my-default.json");

    private sealed record PanelPlacement(double Left, double Width, double Top, double? Height = null);

    private readonly Border[] _slots;
    private readonly Dictionary<Border, Control> _cards = new();
    private readonly Dictionary<Border, PanelResizeGrips> _grips = new();
    private readonly Dictionary<string, PanelPlacement> _defaultLayout;
    private readonly HashSet<Border> _selected = new();
    private Point? _dragStart;
    private Border? _dragSlot;
    private bool _dragging;
    private Point _grabOffset;
    private Border? _swapHighlight;

    public DashboardView()
    {
        InitializeComponent();

        _slots = [SlotTop0, SlotTop1, SlotTop2, SlotTop3, SlotBottom0, SlotBottom1, SlotBottom2];
        foreach (var slot in _slots)
        {
            var card = slot.Child!;
            card.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            var grips = new PanelResizeGrips(slot, Board, SaveLayout);
            slot.Child = null;
            var host = new Panel();
            host.Children.Add(card);
            host.Children.Add(grips);
            slot.Child = host;
            slot.Background = Avalonia.Media.Brushes.Transparent;
            slot.ClipToBounds = true;
            _cards[slot] = card;
            _grips[slot] = grips;

            slot.AddHandler(PointerPressedEvent, Slot_PointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
            slot.AddHandler(PointerMovedEvent, Slot_PointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
            slot.AddHandler(PointerReleasedEvent, Slot_PointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
            slot.PointerCaptureLost += (_, _) => EndDrag(drop: false, null);
        }

        _defaultLayout = CurrentLayout();

        Board.PointerPressed += (_, e) =>
        {
            if (e.Source == Board)
            {
                ClearSelection();
            }
        };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && _selected.Count > 0)
            {
                ClearSelection();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, CopyInfoRow_PointerReleased, RoutingStrategies.Bubble);
        DashboardScroll.SizeChanged += (_, _) => Board.MinHeight = Math.Max(0, DashboardScroll.Viewport.Height - 20);

        ApplyLayout(LoadLayout(LayoutPath));
    }

    private string PanelId(Border slot) => _cards.TryGetValue(slot, out var card) ? card.Tag as string ?? string.Empty : string.Empty;

    private Dictionary<string, PanelPlacement> CurrentLayout() =>
        _slots.ToDictionary(
            PanelId,
            slot => new PanelPlacement(
                DashboardBoard.GetLeft(slot),
                DashboardBoard.GetWidthRatio(slot),
                DashboardBoard.GetTop(slot),
                slot.MinHeight > 0 ? slot.MinHeight : null));

    private void ApplyLayout(IReadOnlyDictionary<string, PanelPlacement>? layout)
    {
        if (layout is null
            || !_slots.All(slot => layout.TryGetValue(PanelId(slot), out var placement) && placement.Width > 0))
        {
            return;
        }

        foreach (var slot in _slots)
        {
            var placement = layout[PanelId(slot)];
            var width = Math.Clamp(placement.Width, DashboardBoard.MinWidthRatio, 1);
            DashboardBoard.SetWidthRatio(slot, width);
            DashboardBoard.SetLeft(slot, Math.Clamp(placement.Left, 0, 1 - width));
            DashboardBoard.SetTop(slot, placement.Top);
            slot.Height = double.NaN;
            slot.MinHeight = placement.Height is double height
                ? Math.Max(PanelResizeGrips.MinPanelHeight, height)
                : 0;
        }
    }

    private static Dictionary<string, PanelPlacement>? LoadLayout(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, PanelPlacement>>(File.ReadAllText(path))
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void SaveLayout() => WriteLayout(LayoutPath);

    private void WriteLayout(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(CurrentLayout()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Layout is a convenience; a failed save keeps the current session layout.
        }
    }

    private void ResetLayout_OnClick(object? sender, RoutedEventArgs e) => ResetLayout(useMyDefault: true);

    /// <summary>Restores the user's saved default layout, or the built-in one when none was saved.</summary>
    public void ResetLayout(bool useMyDefault)
    {
        ClearSelection();
        var layout = useMyDefault ? LoadLayout(MyDefaultPath) : null;
        ApplyLayout(_defaultLayout);
        ApplyLayout(layout);
        SaveLayout();
    }

    public void SaveCurrentAsMyDefault() => WriteLayout(MyDefaultPath);

    private void Slot_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var slot = (Border)sender!;
        if (!e.GetCurrentPoint(slot).Properties.IsLeftButtonPressed || IsInteractive(e.Source as Visual, slot))
        {
            _dragStart = null;
            return;
        }

        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            SetSelected(slot, !_selected.Contains(slot));
            _dragStart = null;
            e.Handled = true;
            return;
        }

        if (!_selected.Contains(slot))
        {
            ClearSelection();
        }

        Focus();
        _dragStart = e.GetPosition(this);
        _grabOffset = e.GetPosition(slot);
        _dragSlot = slot;
    }

    private void Slot_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSlot is null)
        {
            return;
        }

        if (!_dragging)
        {
            if (_dragStart is not Point start || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            var delta = e.GetPosition(this) - start;
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
            {
                return;
            }

            _dragStart = null;
            _dragging = true;
            foreach (var slot in GroupFor(_dragSlot))
            {
                SetOpacity(slot, 0.5);
            }

            e.Pointer.Capture(_dragSlot);
        }

        DragOver(e);
        e.Handled = true;
    }

    private void Slot_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragStart = null;
        if (_dragging)
        {
            EndDrag(drop: true, e);
            e.Pointer.Capture(null);
            e.Handled = true;
        }
        else
        {
            _dragSlot = null;
        }
    }

    private void SetSelected(Border slot, bool selected)
    {
        if (selected)
        {
            _selected.Add(slot);
        }
        else
        {
            _selected.Remove(slot);
        }

        if (_grips.TryGetValue(slot, out var grips))
        {
            grips.IsSelected = selected;
        }
    }

    private void ClearSelection()
    {
        foreach (var slot in _selected.ToList())
        {
            SetSelected(slot, false);
        }
    }

    /// <summary>The panels that move with <paramref name="source"/>: the whole selection when it is part of one.</summary>
    private IReadOnlyList<Border> GroupFor(Border source) =>
        _selected.Count > 1 && _selected.Contains(source) ? _selected.ToList() : [source];

    private Rect Placed(Border slot) => Board.PlacedRect(slot) ?? default;

    /// <summary>
    /// Offset (board-width ratio, pixels) that moves <paramref name="source"/> to the drop spot, limited so
    /// every panel of the group stays inside the board.
    /// </summary>
    private (double Left, double Top) GroupOffset(IReadOnlyList<Border> group, Border source, double left, double top)
    {
        var dLeft = left - DashboardBoard.GetLeft(source);
        var dTop = top - Placed(source).Top;
        var minLeft = group.Min(DashboardBoard.GetLeft);
        var maxRight = group.Max(slot => DashboardBoard.GetLeft(slot) + DashboardBoard.GetWidthRatio(slot));
        dLeft = Math.Clamp(dLeft, -minLeft, Math.Max(-minLeft, 1 - maxRight));
        dTop = Math.Max(dTop, -group.Min(slot => Placed(slot).Top));
        return (dLeft, dTop);
    }

    private (double Left, double Top) DropSpot(PointerEventArgs e, Border source)
    {
        var point = e.GetPosition(Board);
        return Board.Snap(source, new Point(point.X - _grabOffset.X, point.Y - _grabOffset.Y));
    }

    private void DragOver(PointerEventArgs e)
    {
        var source = _dragSlot!;
        var (left, top) = DropSpot(e, source);
        var group = GroupFor(source);
        if (group.Count == 1 && SwapTarget(e, source) is Border target)
        {
            Board.ShowPreview(Placed(target));
            SetSwapHighlight(target);
        }
        else if (group.Count == 1)
        {
            Board.ShowPreview(source, left, top);
            SetSwapHighlight(null);
        }
        else
        {
            var (dLeft, dTop) = GroupOffset(group, source, left, top);
            var span = Board.Span(Board.Bounds.Width);
            Board.ShowPreview(group
                .Select(Placed)
                .Select(rect => new Rect(rect.X + dLeft * span, rect.Y + dTop, rect.Width, rect.Height))
                .ToList());
            SetSwapHighlight(null);
        }

        AutoScroll(e);
    }

    private void EndDrag(bool drop, PointerEventArgs? e)
    {
        if (!_dragging || _dragSlot is null)
        {
            return;
        }

        var source = _dragSlot;
        var group = GroupFor(source);
        _dragging = false;
        _dragSlot = null;

        if (drop && e is not null)
        {
            var (left, top) = DropSpot(e, source);
            var target = group.Count == 1 ? SwapTarget(e, source) : null;
            var (dLeft, dTop) = GroupOffset(group, source, left, top);
            Board.FreezePositions();
            if (target is not null)
            {
                SwapPlacement(source, target);
            }
            else
            {
                foreach (var slot in group)
                {
                    var width = DashboardBoard.GetWidthRatio(slot);
                    DashboardBoard.SetLeft(slot, Math.Clamp(
                        DashboardBoard.SnapRatio(DashboardBoard.GetLeft(slot) + dLeft), 0, Math.Max(0, 1 - width)));
                    DashboardBoard.SetTop(slot, Math.Max(0, DashboardBoard.GetTop(slot) + dTop));
                }
            }

            SaveLayout();
        }

        Board.ClearPreview();
        SetSwapHighlight(null);
        foreach (var slot in group)
        {
            SetOpacity(slot, 1);
        }
    }

    /// <summary>The panel whose middle area (inner half each way) is under the pointer, if any.</summary>
    private Border? SwapTarget(PointerEventArgs e, Border source)
    {
        var point = e.GetPosition(Board);
        foreach (var slot in _slots)
        {
            if (slot == source || Board.PlacedRect(slot) is not Rect rect || rect.Width <= 0)
            {
                continue;
            }

            if (rect.Deflate(new Thickness(rect.Width / 4, rect.Height / 4)).Contains(point))
            {
                return slot;
            }
        }

        return null;
    }

    private static void SwapPlacement(Border a, Border b)
    {
        var (left, width, top, height) =
            (DashboardBoard.GetLeft(a), DashboardBoard.GetWidthRatio(a), DashboardBoard.GetTop(a), a.MinHeight);

        DashboardBoard.SetWidthRatio(a, DashboardBoard.GetWidthRatio(b));
        DashboardBoard.SetLeft(a, DashboardBoard.GetLeft(b));
        DashboardBoard.SetTop(a, DashboardBoard.GetTop(b));
        a.MinHeight = b.MinHeight;

        DashboardBoard.SetWidthRatio(b, width);
        DashboardBoard.SetLeft(b, left);
        DashboardBoard.SetTop(b, top);
        b.MinHeight = height;
    }

    private void SetSwapHighlight(Border? target)
    {
        if (_swapHighlight == target)
        {
            return;
        }

        if (_swapHighlight is not null)
        {
            SetOpacity(_swapHighlight, 1);
        }

        _swapHighlight = target;
        if (target is not null)
        {
            SetOpacity(target, 0.6);
        }
    }

    private void AutoScroll(PointerEventArgs e)
    {
        const double edge = 40;
        const double step = 20;
        var y = e.GetPosition(DashboardScroll).Y;
        var offset = DashboardScroll.Offset;
        if (y < edge)
        {
            DashboardScroll.Offset = offset.WithY(Math.Max(0, offset.Y - step));
        }
        else if (y > DashboardScroll.Viewport.Height - edge)
        {
            DashboardScroll.Offset = offset.WithY(offset.Y + step);
        }
    }

    private void SetOpacity(Border slot, double opacity)
    {
        if (_cards.TryGetValue(slot, out var card))
        {
            card.Opacity = opacity;
        }
    }

    private static bool IsInteractive(Visual? element, Visual stopAt)
    {
        while (element is not null && element != stopAt)
        {
            if (element is Button or ToggleButton or DataGrid or ScrollBar or TextBox or ComboBox or Thumb or PanelResizeGrips)
            {
                return true;
            }

            element = element.GetVisualParent();
        }

        return false;
    }

    private void DiskPartitions_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true) is not null
            && DataContext is MainViewModel vm)
        {
            vm.Disk.OpenSelectedDriveCommand.Execute(null);
        }
    }

    private void DiskPartitions_PointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.Disk.SelectedPartition = null;
        }
    }

    private void CopyInfoRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left
            || (e.Source as Visual)?.FindAncestorOfType<TextBlock>(includeSelf: true) is not { } text
            || !text.Classes.Contains("copy")
            || text.DataContext is not InfoRowViewModel row)
        {
            return;
        }

        if (row.TryCopy())
        {
            ShowCopiedHint(text);
            e.Handled = true;
        }
    }

    private void CopyNetworkValue_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left
            || DataContext is not MainViewModel vm
            || sender is not Control { Tag: string key } element)
        {
            return;
        }

        var value = key switch
        {
            "PublicIp" => vm.Network.PublicIp,
            "LocalIp" => vm.Network.IpAddress,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(value) || value == "-")
        {
            return;
        }

        UiPlatform.SetClipboardText(value);
        ShowCopiedHint(element);
        e.Handled = true;
    }

    private static void ShowCopiedHint(Control target)
    {
        var original = ToolTip.GetTip(target);
        ToolTip.SetTip(target, new TextBlock { Text = "Copied", FontWeight = Avalonia.Media.FontWeight.Bold, FontSize = 12 });
        ToolTip.SetIsOpen(target, true);
        DispatcherTimer.RunOnce(() =>
        {
            ToolTip.SetIsOpen(target, false);
            ToolTip.SetTip(target, original);
        }, TimeSpan.FromMilliseconds(1200));
    }
}
