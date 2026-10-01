using System.Diagnostics;
using System.IO;

namespace MonitorAgent.UI.Services;

/// <summary>Opens files and folders with the desktop's own file manager or default app.</summary>
public static class ShellOpen
{
    /// <summary>Opens a file with its default app, or a folder in the file manager.</summary>
    public static bool Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Shell] Could not open {path}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Shows the file selected in the file manager (Linux opens its folder).</summary>
    public static void Reveal(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path.Replace("\"", string.Empty) + "\"") { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-R", path } });
            }
            else if (Path.GetDirectoryName(path) is { Length: > 0 } folder)
            {
                Open(folder);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Shell] Could not show {path}: {ex.Message}");
        }
    }
}
