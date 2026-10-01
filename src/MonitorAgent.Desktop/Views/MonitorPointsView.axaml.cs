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
    public MonitorPointsView()
    {
        InitializeComponent();
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
