using Avalonia.Controls;
using Avalonia.Interactivity;
using MonitorAgent.UI.ViewModels;

namespace MonitorAgent.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void Reset_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not SettingsViewModel settings)
        {
            return;
        }

        var answer = await ThemedDialog.ShowAsync(
            TopLevel.GetTopLevel(this) as Window,
            "Reset changes",
            $"All unsaved changes in \"{settings.SelectedSectionTitle}\" will be lost, and this tab will go back to its last saved values.\n\nDo you want to continue?",
            DialogKind.Warning,
            [
                new DialogButton("Reset", DialogResult.Yes, IsDanger: true),
                new DialogButton("Cancel", DialogResult.No)
            ],
            DialogResult.No);
        if (answer == DialogResult.Yes)
        {
            settings.ResetCommand.Execute(null);
        }
    }

    private async void ConfigureDatabase_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not MonitorPointSettingViewModel row
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var client = ((DataContext as MainViewModel)?.Settings ?? DataContext as SettingsViewModel)?.Client;
        var dialog = new DatabaseLoginWindow(row.DatabaseLogin?.Copy(), client);
        if (await dialog.ShowDialog<bool>(owner) && dialog.Result is not null)
        {
            row.DatabaseLogin = dialog.Result;
        }
    }
}
