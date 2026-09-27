using System.IO;
using Microsoft.Win32;

namespace ClientAgent.UI.Services;

public sealed class InstalledAppInfo
{
    public string Name { get; init; } = string.Empty;

    public string ExecutablePath { get; init; } = string.Empty;

    public string ProcessName { get; init; } = string.Empty;

    public string? IconBase64 { get; init; }
}

public static class InstalledProgramCatalog
{
    private static readonly string[] Roots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    private static IReadOnlyList<InstalledAppInfo> _apps = [];

    public static IReadOnlyList<InstalledAppInfo> Apps => _apps;

    public static InstalledAppInfo? Find(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return _apps.FirstOrDefault(app => string.Equals(app.ExecutablePath, path, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<InstalledAppInfo> Load()
    {
        var found = new Dictionary<string, InstalledAppInfo>(StringComparer.OrdinalIgnoreCase);
        ReadHive(Registry.LocalMachine, found);
        ReadHive(Registry.CurrentUser, found);
        _apps = found.Values.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return _apps;
    }

    private static void ReadHive(RegistryKey hive, Dictionary<string, InstalledAppInfo> found)
    {
        foreach (var root in Roots)
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
                    var app = ReadApp(sub);
                    if (app is not null)
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

    private static InstalledAppInfo? ReadApp(RegistryKey? key)
    {
        if (key is null || key.GetValue("SystemComponent") is int system && system == 1)
        {
            return null;
        }

        var name = key.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(name) || IsNoise(name))
        {
            return null;
        }

        var iconPath = ParsePath(key.GetValue("DisplayIcon") as string);
        var installLocation = ParsePath(key.GetValue("InstallLocation") as string);
        var executable = ResolveExecutable(iconPath, installLocation, name);
        if (executable is null)
        {
            return null;
        }

        return new InstalledAppInfo
        {
            Name = name.Trim(),
            ExecutablePath = executable,
            ProcessName = Path.GetFileNameWithoutExtension(executable),
            IconBase64 = ReadIcon(executable)
        };
    }

    private static string? ResolveExecutable(string? iconPath, string? installLocation, string displayName)
    {
        if (IsLaunchExecutable(iconPath))
        {
            return iconPath;
        }

        if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
        {
            return IsLaunchExecutable(iconPath) ? iconPath : null;
        }

        string? fallback = null;
        foreach (var file in EnumerateExecutables(installLocation))
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

    private static IEnumerable<string> EnumerateExecutables(string directory)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*.exe", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return files.Where(IsLaunchExecutable);
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

    private static string? ParsePath(string? value)
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

        return File.Exists(text) ? text : text;
    }

    private static string? ReadIcon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return Convert.ToBase64String(stream.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    private static bool IsNoise(string name)
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
