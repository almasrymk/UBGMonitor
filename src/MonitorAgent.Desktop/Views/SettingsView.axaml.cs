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

    private async void SaveSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel settings) return;
        if (settings.SelectedSection == SettingsViewModel.SectionMonitorPoints &&
            (!MonitorPointsGrid.CommitEdit(DataGridEditingUnit.Cell, true) ||
             !MonitorPointsGrid.CommitEdit(DataGridEditingUnit.Row, true))) return;
        if (settings.SaveCommand.CanExecute(null)) await settings.SaveCommand.ExecuteAsync(null);
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
    private async void CopyRemoteKey_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string role } button) await CopyRemoteKeyAndShowHintAsync(button, role);
    }
    private async void CopyConnectionValue_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind } button) return;
        var settings = (DataContext as MainViewModel)?.Settings ?? DataContext as SettingsViewModel;
        if (settings is null) return;
        var value = kind switch { "url" => settings.ApiBaseUrl, "fingerprint" => settings.CertificateFingerprint, _ => settings.ClientAccessKey };
        if (string.IsNullOrWhiteSpace(value)) return;
        await MonitorAgent.UI.Services.UiPlatform.CopySensitiveTextAsync(value, () =>
        {
            ToolTip.SetTip(button, new TextBlock { Text = "Copied", FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Avalonia.Media.Brushes.LimeGreen });
            ToolTip.SetIsOpen(button, true);
            Avalonia.Threading.DispatcherTimer.RunOnce(() => { ToolTip.SetIsOpen(button, false); ToolTip.SetTip(button, "Click to copy"); }, TimeSpan.FromMilliseconds(1200));
        });
    }
    private async void RemoteKey_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (sender is not TextBlock label || e.InitialPressMouseButton is not
            (Avalonia.Input.MouseButton.Left or Avalonia.Input.MouseButton.Right)) return;
        var settings = (DataContext as MainViewModel)?.Settings ?? DataContext as SettingsViewModel;
        if (settings?.CanManageRemoteAccess != true || label.Tag is not string role) return;
        e.Handled = true;
        await CopyRemoteKeyAndShowHintAsync(label, role);
    }
    private async Task CopyRemoteKeyAndShowHintAsync(Control label, string role)
    {
        var settings = (DataContext as MainViewModel)?.Settings ?? DataContext as SettingsViewModel;
        if (settings?.CanManageRemoteAccess != true) return;
        void Copied(string copiedRole)
        {
            if (copiedRole != role) return;
            var hint = new TextBlock { Text = "Copied", FontWeight = Avalonia.Media.FontWeight.Bold, FontSize = 12 };
            hint.Foreground = Avalonia.Media.Brushes.LimeGreen;
            ToolTip.SetTip(label, hint);
            ToolTip.SetIsOpen(label, true);
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                ToolTip.SetIsOpen(label, false);
                ToolTip.SetTip(label, "Click to copy");
            }, TimeSpan.FromMilliseconds(1200));
        }
        settings.RemoteKeyCopied += Copied;
        try { await settings.CopyRemoteKeyCommand.ExecuteAsync(role); }
        finally { settings.RemoteKeyCopied -= Copied; }
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
        if (stream.CanSeek && stream.Length > 1024 * 1024) { settings.StatusMessage = "Certificate file exceeds the size limit."; return; }
        var password = new TextBox { PasswordChar = '●', Watermark = "PFX password (leave empty if none)" };
        var apply = new Button { Content = "Import", Margin = new Avalonia.Thickness(0,12,0,0) };
        var dialog = new Window { Title = "Import customer certificate", Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Avalonia.Thickness(20), Children = { new TextBlock { Text = "Import this PFX for remote HTTPS. Paired clients may need to verify its new fingerprint.", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, password, apply } } };
        apply.Click += (_, _) => dialog.Close(true);
        if (!await dialog.ShowDialog<bool>(owner)) return;
        using var bytes = new System.IO.MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            if (bytes.Length + read > 1024 * 1024)
            {
                password.Text = string.Empty;
                settings.StatusMessage = "Certificate file exceeds the size limit.";
                return;
            }
            await bytes.WriteAsync(buffer.AsMemory(0, read));
        }
        var result = await settings.Client.ManageRemoteAccessAsync(new MonitorAgent.Shared.Models.RemoteAccessAction("import-certificate", Pfx: Convert.ToBase64String(bytes.ToArray()), PfxPassword: password.Text));
        password.Text = string.Empty;
        settings.StatusMessage = result is null ? "Certificate import failed. Check its password, private key and expiration." : "Customer certificate imported. Reconnect remote clients after verifying the fingerprint.";
        if (result is not null) settings.UpdateCertificateDisplay(result.Status);
    }
}
