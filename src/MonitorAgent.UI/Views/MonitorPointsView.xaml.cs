using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Models.Reports;
using MonitorAgent.UI.ViewModels;

namespace MonitorAgent.UI.Views;

public partial class MonitorPointsView : UserControl
{
    public MonitorPointsView()
    {
        InitializeComponent();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel main
            && ItemsControl.ContainerFromElement((DataGrid)sender, (DependencyObject)e.OriginalSource) is DataGridRow { Item: MonitorPointStatusDto point })
        {
            _ = main.Reports.OpenDetailsAsync(ReportTypes.PointSubjectPrefix + point.MonitorPointId);
        }
    }
}
