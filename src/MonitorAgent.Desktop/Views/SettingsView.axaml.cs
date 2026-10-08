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
    private async void ImportCertificate_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var settings = (DataContext as MainViewModel)?.Settings ?? DataContext as SettingsViewModel;
        if (settings?.CanManageRemoteAccess != true) return;
        var files = await owner.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Customer PFX certificate", AllowMultiple = false,
            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("PFX certificate") { Patterns = ["*.pfx", "*.p12"] }]
        });
        if (files.Count == 0) return;
        await using var stream = await files[0].OpenReadAsync();
        if (stream.Length > 1024 * 1024) { settings.StatusMessage = "Certificate file exceeds the size limit."; return; }
        var password = new TextBox { PasswordChar = '●', Watermark = "PFX password (leave empty if none)" };
        var apply = new Button { Content = "Import", Margin = new Avalonia.Thickness(0,12,0,0) };
        var dialog = new Window { Title = "Import customer certificate", Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Avalonia.Thickness(20), Children = { new TextBlock { Text = "Import this PFX for remote HTTPS. Paired clients may need to verify its new fingerprint.", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, password, apply } } };
        apply.Click += (_, _) => dialog.Close(true);
        if (!await dialog.ShowDialog<bool>(owner)) return;
        using var bytes = new System.IO.MemoryStream(); await stream.CopyToAsync(bytes);
        var result = await settings.Client.ManageRemoteAccessAsync(new MonitorAgent.Shared.Models.RemoteAccessAction("import-certificate", Pfx: Convert.ToBase64String(bytes.ToArray()), PfxPassword: password.Text));
        password.Text = string.Empty;
        settings.StatusMessage = result is null ? "Certificate import failed. Check its password, private key and expiration." : "Customer certificate imported. Reconnect remote clients after verifying the fingerprint.";
        if (result is not null) settings.RemoteFingerprint = "Certificate SHA-256: " + result.Status.Fingerprint;
    }
}
