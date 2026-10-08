using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace MonitorAgent.UI.Services;

/// <summary>Clipboard and file dialogs for view models.</summary>
public static class UiPlatform
{
    private static TopLevel? Main =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public static bool SetClipboardText(string text)
    {
        if (Main?.Clipboard is not { } clipboard)
        {
            return false;
        }

        _ = clipboard.SetTextAsync(text);
        return true;
    }
    public static async Task CopySensitiveTextAsync(string text, Action? onCopied = null)
    {
        if (Main?.Clipboard is not { } clipboard) return;
        try
        {
            await SensitiveClipboard.CopyAsync(text, async value =>
            {
                var data = new DataTransfer();
                var item = DataTransferItem.CreateText(value);
                if (OperatingSystem.IsWindows())
                {
                    item.Set(DataFormat.CreateBytesPlatformFormat("CanIncludeInClipboardHistory"), new byte[4]);
                    item.Set(DataFormat.CreateBytesPlatformFormat("CanUploadToCloudClipboard"), new byte[4]);
                }
                data.Add(item);
                  await clipboard.SetDataAsync(data);
                  onCopied?.Invoke();
            }, () => clipboard.TryGetTextAsync(), () => clipboard.ClearAsync());
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }

    public static void ShowMessage(string title, string text)
    {
        var ok = new Button { Content = "OK", MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                    ok
                }
            }
        };
        ok.Click += (_, _) => dialog.Close();
        if (Main is Window owner)
        {
            _ = dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }
    }

    public static async Task<string?> PickSaveFileAsync(string fileTypeName, string extension, string suggestedName)
    {
        if (Main?.StorageProvider is not { CanSave: true } storage)
        {
            return null;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
            DefaultExtension = extension.TrimStart('.'),
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(fileTypeName) { Patterns = ["*" + extension] }]
        });
        return file?.TryGetLocalPath();
    }
}
