using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

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
