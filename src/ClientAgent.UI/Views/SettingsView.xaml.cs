using System.Windows;
using System.Windows.Controls;
using ClientAgent.UI.ViewModels;

namespace ClientAgent.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void ConfigureDatabase_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not MonitorPointSettingViewModel row)
        {
            return;
        }

        var dialog = new DatabaseLoginWindow(row.DatabaseLogin?.Copy());
        dialog.Owner = Window.GetWindow(this);
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            row.DatabaseLogin = dialog.Result;
        }
    }
}
