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

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not SettingsViewModel settings)
        {
            return;
        }

        var answer = MessageBox.Show(
            Window.GetWindow(this)!,
            $"All unsaved changes in \"{settings.SelectedSectionTitle}\" will be lost, and this tab will go back to its last saved values.\n\nDo you want to continue?",
            "Reset changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
        {
            settings.ResetCommand.Execute(null);
        }
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
