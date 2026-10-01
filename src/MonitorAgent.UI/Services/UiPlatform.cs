using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace MonitorAgent.UI.Services;

/// <summary>Clipboard and file dialogs; each app (WPF, Avalonia) has its own version of this class.</summary>
public static class UiPlatform
{
    public static bool SetClipboardText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <returns>The chosen path, or null when cancelled.</returns>
    public static Task<string?> PickSaveFileAsync(string fileTypeName, string extension, string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = $"{fileTypeName} (*{extension})|*{extension}",
            DefaultExt = extension,
            FileName = suggestedName
        };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }
}
