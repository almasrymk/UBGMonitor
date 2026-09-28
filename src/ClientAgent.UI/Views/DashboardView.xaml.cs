using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClientAgent.UI.ViewModels;

namespace ClientAgent.UI.Views;

public partial class DashboardView : UserControl
{
    private const string PanelDragFormat = "AgentMonitor.DashboardPanel";

    private static readonly string LayoutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgentMonitor",
        "dashboard-layout.json");

    private readonly Border[] _slots;
    private readonly string[] _defaultLayout;
    private Point? _dragStart;
    private Border? _dragSlot;
    private Border? _hoverSlot;

    public DashboardView()
    {
        InitializeComponent();

        _slots = [SlotTop0, SlotTop1, SlotTop2, SlotTop3, SlotBottom0, SlotBottom1, SlotBottom2];
        _defaultLayout = CurrentLayout();

        foreach (var slot in _slots)
        {
            slot.Background = Brushes.Transparent;
            slot.AllowDrop = true;
            slot.PreviewMouseLeftButtonDown += Slot_OnPreviewMouseLeftButtonDown;
            slot.PreviewMouseMove += Slot_OnPreviewMouseMove;
            slot.PreviewMouseLeftButtonUp += (_, _) => _dragStart = null;
            slot.DragOver += Slot_OnDragOver;
            slot.Drop += Slot_OnDrop;
        }

        ApplyLayout(LoadLayout());
    }

    private string[] CurrentLayout() =>
        _slots.Select(slot => (slot.Child as FrameworkElement)?.Tag as string ?? string.Empty).ToArray();

    private void ApplyLayout(IReadOnlyList<string>? layout)
    {
        if (layout is null
            || layout.Count != _slots.Length
            || !layout.OrderBy(id => id).SequenceEqual(_defaultLayout.OrderBy(id => id)))
        {
            return;
        }

        var panels = _slots
            .Select(slot => slot.Child as FrameworkElement)
            .OfType<FrameworkElement>()
            .ToDictionary(panel => (string)panel.Tag);

        foreach (var slot in _slots)
        {
            slot.Child = null;
        }

        for (var i = 0; i < _slots.Length; i++)
        {
            _slots[i].Child = panels[layout[i]];
        }
    }

    private static string[]? LoadLayout()
    {
        try
        {
            return File.Exists(LayoutPath)
                ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(LayoutPath))
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void SaveLayout()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath)!);
            File.WriteAllText(LayoutPath, JsonSerializer.Serialize(CurrentLayout()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Layout is a convenience; a failed save keeps the current session layout.
        }
    }

    private void ResetLayout_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyLayout(_defaultLayout);
        SaveLayout();
    }

    private void Slot_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var slot = (Border)sender;
        if (IsInteractive(e.OriginalSource as DependencyObject, slot))
        {
            _dragStart = null;
            return;
        }

        _dragStart = e.GetPosition(this);
        _dragSlot = slot;
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
        _dragStart = null;
        SetOpacity(source, 0.5);
        try
        {
            DragDrop.DoDragDrop(source, new DataObject(PanelDragFormat, source), DragDropEffects.Move);
        }
        finally
        {
            SetOpacity(source, 1);
            SetHover(null);
            _dragSlot = null;
        }
    }

    private void Slot_OnDragOver(object sender, DragEventArgs e)
    {
        var target = (Border)sender;
        var valid = e.Data.GetData(PanelDragFormat) is Border source && source != target;
        e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
        SetHover(valid ? target : null);
        e.Handled = true;
    }

    private void Slot_OnDrop(object sender, DragEventArgs e)
    {
        var target = (Border)sender;
        if (e.Data.GetData(PanelDragFormat) is not Border source || source == target)
        {
            return;
        }

        var sourcePanel = source.Child;
        var targetPanel = target.Child;
        source.Child = null;
        target.Child = null;
        source.Child = targetPanel;
        target.Child = sourcePanel;

        SetOpacity(source, 1);
        SetHover(null);
        SaveLayout();
        e.Handled = true;
    }

    private void SetHover(Border? slot)
    {
        if (_hoverSlot == slot)
        {
            return;
        }

        if (_hoverSlot is not null)
        {
            SetOpacity(_hoverSlot, 1);
        }

        _hoverSlot = slot;
        if (slot is not null)
        {
            SetOpacity(slot, 0.7);
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
        var tip = new ToolTip
        {
            Content = new TextBlock
            {
                Text = "Copied",
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 12
            },
            Placement = PlacementMode.Top,
            PlacementTarget = target,
            HorizontalOffset = 0,
            VerticalOffset = -2,
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 5, 10, 5),
            FontWeight = FontWeights.Bold,
            HasDropShadow = true
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
