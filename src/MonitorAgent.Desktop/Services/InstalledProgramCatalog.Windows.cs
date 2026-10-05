using System.Runtime.Versioning;
using MonitorAgent.Desktop.Services;
using Microsoft.Win32;

namespace MonitorAgent.UI.Services;

public static partial class InstalledProgramCatalog
{
    private static readonly string[] UninstallRoots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    [SupportedOSPlatform("windows")]
    private static void ReadWindowsPrograms(Dictionary<string, InstalledAppInfo> found)
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var root in UninstallRoots)
            {
                using var key = hive.OpenSubKey(root);
                if (key is null)
                {
                    continue;
                }

                foreach (var name in key.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = key.OpenSubKey(name);
                        if (ReadWindowsProgram(sub) is { } app)
                        {
                            found.TryAdd(app.ExecutablePath, app);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    {
                    }
                }
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static InstalledAppInfo? ReadWindowsProgram(RegistryKey? key)
    {
        if (key is null || key.GetValue("SystemComponent") is int system && system == 1)
        {
            return null;
        }

        var name = key.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(name) || IsUpdateEntry(name))
        {
            return null;
        }

        var executable = ResolveWindowsExecutable(
            ParseRegistryPath(key.GetValue("DisplayIcon") as string),
            ParseRegistryPath(key.GetValue("InstallLocation") as string),
            name);
        if (executable is null)
        {
            return null;
        }

        return new InstalledAppInfo
        {
            Name = name.Trim(),
            ExecutablePath = executable,
            ProcessName = Path.GetFileNameWithoutExtension(executable),
            IconBase64 = ReadWindowsIcon(executable)
        };
    }

    private static string? ResolveWindowsExecutable(string? iconPath, string? installLocation, string displayName)
    {
        if (IsLaunchExecutable(iconPath))
        {
            return iconPath;
        }

        if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
        {
            return null;
        }

        string? fallback = null;
        foreach (var file in SafeFiles(installLocation, "*.exe").Where(IsLaunchExecutable))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (displayName.Contains(fileName, StringComparison.OrdinalIgnoreCase)
                || fileName.Contains(FirstWord(displayName), StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }

            fallback ??= file;
        }

        return fallback;
    }

    private static bool IsLaunchExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        return !ContainsAny(name, "uninstall", "unins", "setup", "update", "crash", "reporter");
    }

    /// <summary>A path from DisplayIcon or InstallLocation, without quotes or a trailing ",index".</summary>
    private static string? ParseRegistryPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim().Trim('"');
        var comma = text.LastIndexOf(',');
        if (comma > 2)
        {
            var candidate = text[..comma].Trim().Trim('"');
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return text;
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadWindowsIcon(string path)
    {
        try
        {
            using var bitmap = WindowsIcons.Associated(path);
            if (bitmap is null)
            {
                return null;
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream);
            return Convert.ToBase64String(stream.ToArray());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static bool IsUpdateEntry(string name)
        => ContainsAny(name, "security update", "update for", "hotfix", "kb")
           && name.Contains("update", StringComparison.OrdinalIgnoreCase);

    private static string FirstWord(string name)
    {
        var word = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;
        return word.Length < 3 ? name : word;
    }

    private static bool ContainsAny(string text, params string[] parts)
        => parts.Any(part => text.Contains(part, StringComparison.OrdinalIgnoreCase));
}
