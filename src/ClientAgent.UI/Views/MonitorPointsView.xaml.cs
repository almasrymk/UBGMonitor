using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClientAgent.Shared.Models;
using ClientAgent.Shared.Models.Reports;
using ClientAgent.UI.ViewModels;

namespace ClientAgent.UI.Views;

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
