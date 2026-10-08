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
        if (client?.CanAdminister != true) return;
        var dialog = new DatabaseLoginWindow(row.DatabaseLogin?.Copy(), client, row.MonitorPointId);
        if (await dialog.ShowDialog<bool>(owner) && dialog.Result is not null)
        {
            row.DatabaseLogin = dialog.Result;
        }
    }
    private async void CopyPassword_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not MonitorPointSettingViewModel row || TopLevel.GetTopLevel(this) is not Window owner) return;
        var client = ((DataContext as MainViewModel)?.Settings ?? DataContext as SettingsViewModel)?.Client;
        if (client?.CanAdminister != true) return;
        if (await ThemedDialog.ShowAsync(owner, "Copy saved password", "Other applications can read the clipboard. Copy the service-saved password explicitly? MonitorAgent clears it after 30 seconds if it has not changed.", DialogKind.Warning,
            [new DialogButton("Copy password", DialogResult.Yes), new DialogButton("Cancel", DialogResult.Cancel, IsPrimary: true)]) != DialogResult.Yes) return;
        var password = await client.RevealPasswordAsync(row.MonitorPointId);
        if (password is null) { MonitorAgent.UI.Services.UiPlatform.ShowMessage("Copy password", "No saved password could be read. Save this monitor point first."); return; }
        await MonitorAgent.UI.Services.UiPlatform.CopySensitiveTextAsync(password);
    }
}
