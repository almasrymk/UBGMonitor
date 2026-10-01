using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MonitorAgent.UI.Controls;
using MonitorAgent.UI.ViewModels;

namespace MonitorAgent.UI.Views;

public partial class DashboardView : UserControl
{
    private const string PanelDragFormat = "MonitorAgent.DashboardPanel";

    private static readonly string LayoutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MonitorAgent",
        "dashboard-layout.json");

    private static readonly string MyDefaultPath = Path.Combine(
        Path.GetDirectoryName(LayoutPath)!,
        "dashboard-my-default.json");

    private sealed record PanelPlacement(double Left, double Width, double Top, double? Height = null);

    private readonly Border[] _slots;
    private readonly Dictionary<string, PanelPlacement> _defaultLayout;
    private Point? _dragStart;
    private Border? _dragSlot;
    private Point _grabOffset;
    private Border? _swapHighlight;
    private readonly HashSet<Border> _selected = new();
    private readonly Dictionary<Border, PanelResizeAdorner> _adorners = new();

    public DashboardView()
    {
        InitializeComponent();
        Focusable = true;
        FocusVisualStyle = null;

        _slots = [SlotTop0, SlotTop1, SlotTop2, SlotTop3, SlotBottom0, SlotBottom1, SlotBottom2];
        _defaultLayout = CurrentLayout();

        foreach (var slot in _slots)
        {
            slot.Background = Brushes.Transparent;
            slot.ClipToBounds = true;
            if (slot.Child is FrameworkElement card)
            {
                card.VerticalAlignment = VerticalAlignment.Stretch;
            }

            slot.PreviewMouseLeftButtonDown += Slot_OnPreviewMouseLeftButtonDown;
            slot.PreviewMouseMove += Slot_OnPreviewMouseMove;
            slot.PreviewMouseLeftButtonUp += (_, _) => _dragStart = null;
        }

        Board.DragOver += Board_OnDragOver;
        Board.DragLeave += (_, e) =>
        {
            var point = e.GetPosition(Board);
            if (point.X < 0 || point.Y < 0 || point.X > Board.ActualWidth || point.Y > Board.ActualHeight)
            {
                Board.ClearPreview();
            }
        };
        Board.Drop += Board_OnDrop;
        Board.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource == Board)
            {
                ClearSelection();
            }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _selected.Count > 0)
            {
                ClearSelection();
                e.Handled = true;
            }
        };
        DashboardScroll.SizeChanged += (_, _) => Board.MinHeight = Math.Max(0, DashboardScroll.ViewportHeight - 20);

        ApplyLayout(LoadLayout(LayoutPath));
        Loaded += (_, _) => AttachResizeGrips();
    }

    private bool _gripsAttached;

    private void AttachResizeGrips()
    {
        var layer = AdornerLayer.GetAdornerLayer(Board);
        if (_gripsAttached || layer is null)
        {
            return;
        }

        var style = TryFindResource("PanelResizeGrip") as Style;
        foreach (var slot in _slots)
        {
            var adorner = new PanelResizeAdorner(slot, Board, style, SaveLayout);
            _adorners[slot] = adorner;
            layer.Add(adorner);
        }

        _gripsAttached = true;
    }

    private static string PanelId(Border slot) => (slot.Child as FrameworkElement)?.Tag as string ?? string.Empty;

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
                ? Math.Max(PanelResizeAdorner.MinPanelHeight, height)
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

    private void ResetLayout_OnClick(object sender, RoutedEventArgs e) => ResetLayout(useMyDefault: true);

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

    private void Slot_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var slot = (Border)sender;
        if (IsInteractive(e.OriginalSource as DependencyObject, slot))
        {
            _dragStart = null;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
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

        if (_adorners.TryGetValue(slot, out var adorner))
        {
            adorner.IsSelected = selected;
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

    /// <summary>
    /// Offset (board-width ratio, pixels) that moves <paramref name="source"/> to the drop spot, limited so
    /// every panel of the group stays inside the board.
    /// </summary>
    private (double Left, double Top) GroupOffset(IReadOnlyList<Border> group, Border source, double left, double top)
    {
        var dLeft = left - DashboardBoard.GetLeft(source);
        var dTop = top - Board.PlacedRect(source).Top;
        var minLeft = group.Min(DashboardBoard.GetLeft);
        var maxRight = group.Max(slot => DashboardBoard.GetLeft(slot) + DashboardBoard.GetWidthRatio(slot));
        dLeft = Math.Clamp(dLeft, -minLeft, Math.Max(-minLeft, 1 - maxRight));
        dTop = Math.Max(dTop, -group.Min(slot => Board.PlacedRect(slot).Top));
        return (dLeft, dTop);
    }

    private void Slot_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not Point start || _dragSlot is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var source = _dragSlot;
        var group = GroupFor(source);
        _dragStart = null;
        foreach (var slot in group)
        {
            SetOpacity(slot, 0.5);
        }

        try
        {
            DragDrop.DoDragDrop(source, new DataObject(PanelDragFormat, source), DragDropEffects.Move);
        }
        finally
        {
            foreach (var slot in group)
            {
                SetOpacity(slot, 1);
            }

            SetSwapHighlight(null);
            Board.ClearPreview();
            _dragSlot = null;
        }
    }

    private bool TryDropTarget(DragEventArgs e, out Border source, out double left, out double top)
    {
        left = 0;
        top = 0;
        source = null!;
        if (e.Data.GetData(PanelDragFormat) is not Border slot)
        {
            return false;
        }

        source = slot;
        var point = e.GetPosition(Board);
        (left, top) = Board.Snap(slot, new Point(point.X - _grabOffset.X, point.Y - _grabOffset.Y));
        return true;
    }

    private void Board_OnDragOver(object sender, DragEventArgs e)
    {
        if (TryDropTarget(e, out var source, out var left, out var top))
        {
            e.Effects = DragDropEffects.Move;
            var group = GroupFor(source);
            if (group.Count == 1 && SwapTarget(e, source) is Border target)
            {
                Board.ShowPreview(Board.PlacedRect(target));
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
                var span = Board.Span(Board.ActualWidth);
                Board.ShowPreview(group
                    .Select(slot => Board.PlacedRect(slot))
                    .Select(rect => new Rect(rect.X + dLeft * span, rect.Y + dTop, rect.Width, rect.Height))
                    .ToList());
                SetSwapHighlight(null);
            }

            AutoScroll(e);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void Board_OnDrop(object sender, DragEventArgs e)
    {
        if (!TryDropTarget(e, out var source, out var left, out var top))
        {
            return;
        }

        var group = GroupFor(source);
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

        Board.ClearPreview();
        SetSwapHighlight(null);
        foreach (var slot in group)
        {
            SetOpacity(slot, 1);
        }
        SaveLayout();
        e.Handled = true;
    }

    /// <summary>The panel whose middle area (inner half each way) is under the mouse, if any.</summary>
    private Border? SwapTarget(DragEventArgs e, Border source)
    {
        var point = e.GetPosition(Board);
        foreach (var slot in _slots)
        {
            if (slot == source)
            {
                continue;
            }

            var rect = Board.PlacedRect(slot);
            if (rect.IsEmpty)
            {
                continue;
            }

            rect.Inflate(-rect.Width / 4, -rect.Height / 4);
            if (rect.Contains(point))
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

    private void AutoScroll(DragEventArgs e)
    {
        const double edge = 40;
        const double step = 20;
        var y = e.GetPosition(DashboardScroll).Y;
        if (y < edge)
        {
            DashboardScroll.ScrollToVerticalOffset(DashboardScroll.VerticalOffset - step);
        }
        else if (y > DashboardScroll.ViewportHeight - edge)
        {
            DashboardScroll.ScrollToVerticalOffset(DashboardScroll.VerticalOffset + step);
        }
    }

    private static void SetOpacity(Border slot, double opacity)
    {
        if (slot.Child is UIElement panel)
        {
            panel.Opacity = opacity;
        }
    }

    private static bool IsInteractive(DependencyObject? element, DependencyObject stopAt)
    {
        while (element is not null && element != stopAt)
        {
            if (element is ButtonBase or DataGrid or ScrollBar or TextBoxBase or ComboBox or Thumb)
            {
                return true;
            }

            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }

    private void DiskPartitions_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<DataGridRow>(e.OriginalSource as DependencyObject) is null)
        {
            return;
        }

        if (DataContext is MainViewModel vm)
        {
            vm.Disk.OpenSelectedDriveCommand.Execute(null);
        }
    }

    private void DiskPartitions_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.Disk.SelectedPartition = null;
        }
    }

    private void CopyInfoRow_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: InfoRowViewModel row } element && row.TryCopy())
        {
            ShowCopiedHint(element);
            e.Handled = true;
        }
    }

    private void CopyNetworkValue_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not FrameworkElement { Tag: string key } element)
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

        try
        {
            Clipboard.SetText(value);
            ShowCopiedHint(element);
            e.Handled = true;
        }
        catch
        {
            // Clipboard can be locked by another process.
        }
    }

    private static void ShowCopiedHint(FrameworkElement target)
    {
        var text = new TextBlock { Text = "Copied", FontWeight = FontWeights.Bold, FontSize = 12 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var tip = new ToolTip
        {
            Content = text,
            Placement = PlacementMode.Top,
            PlacementTarget = target,
            HorizontalOffset = 0,
            VerticalOffset = -2,
            FontWeight = FontWeights.Bold
        };

        tip.IsOpen = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            tip.IsOpen = false;
        };
        timer.Start();
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}
