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
    public DashboardView()
    {
        InitializeComponent();
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
