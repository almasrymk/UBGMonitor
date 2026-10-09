using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Models.Reports;
using MonitorAgent.UI.ViewModels;

namespace MonitorAgent.Desktop.Views;

public partial class MonitorPointsView : UserControl
{
    private MainViewModel? _main;
    private string _statusFilter = "All";
    public MonitorPointsView()
    {
        InitializeComponent();
        PointTypeFilter.ItemsSource = new[] { "All Types", "Website", "Device", "Application", "Database" };
        PointTypeFilter.SelectedIndex = 0;
        AttachedToVisualTree += (_, _) => ConnectSource();
        DetachedFromVisualTree += (_, _) => { if (_main is not null) _main.MonitorPoints.CollectionChanged -= Points_Changed; _main = null; };
        DataContextChanged += (_, _) => ConnectSource();
    }

    private void ConnectSource()
    {
        if (_main is not null) _main.MonitorPoints.CollectionChanged -= Points_Changed;
        _main = DataContext as MainViewModel;
        if (_main is not null) _main.MonitorPoints.CollectionChanged += Points_Changed;
        RefreshPoints();
    }
    private void Points_Changed(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RefreshPoints();
    private void Filters_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => RefreshPoints();
    private void StatusFilter_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string filter) return;
        _statusFilter = filter;
        foreach (var item in new[] { AllFilter, HealthyFilter, WarningFilter, ProblemFilter }) item.Classes.Set("selected", Equals(item.Tag, filter));
        RefreshPoints();
    }
    private static string StatusGroup(string? status) => status?.Contains("Warning", StringComparison.OrdinalIgnoreCase) == true ? "Warning"
        : status?.Contains("Healthy", StringComparison.OrdinalIgnoreCase) == true || string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase) ? "Healthy"
        : status?.Contains("Problem", StringComparison.OrdinalIgnoreCase) == true || status?.Contains("Critical", StringComparison.OrdinalIgnoreCase) == true ? "Problem" : "Unknown";
    private void RefreshPoints()
    {
        if (PointsGrid is null || PointSearch is null || PointTypeFilter is null) return;
        var all = _main?.MonitorPoints.ToArray() ?? [];
        var search = PointSearch.Text?.Trim() ?? "";
        var type = PointTypeFilter.SelectedItem as string ?? "All Types";
        var filtered = all.Where(point => (type == "All Types" || point.Type.ToString() == type)
            && (_statusFilter == "All" || StatusGroup(point.Status) == _statusFilter)
            && (search.Length == 0 || new[] { point.MonitorPointId, point.DisplayName, point.Target, point.Location }
                .Any(value => value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true))).ToArray();
        PointsGrid.ItemsSource = filtered;
        PointSummary.Text = $"Showing {filtered.Length} of {all.Length} monitor points";
        AllFilter.Content = $"All ({all.Length})";
        HealthyCount.Text = $"Healthy ({all.Count(point => StatusGroup(point.Status) == "Healthy")})";
        WarningCount.Text = $"Warning ({all.Count(point => StatusGroup(point.Status) == "Warning")})";
        ProblemCount.Text = $"Problem ({all.Count(point => StatusGroup(point.Status) == "Problem")})";
    }
    private void Grid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel main
            && (e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true) is { DataContext: MonitorPointStatusDto point })
        {
            _ = main.Reports.OpenDetailsAsync(ReportTypes.PointSubjectPrefix + point.MonitorPointId);
        }
    }
}
