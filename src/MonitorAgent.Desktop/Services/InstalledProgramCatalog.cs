namespace MonitorAgent.UI.Services;

public sealed class InstalledAppInfo
{
    public string Name { get; init; } = string.Empty;

    public string ExecutablePath { get; init; } = string.Empty;

    public string ProcessName { get; init; } = string.Empty;

    public string? IconBase64 { get; init; }
}

/// <summary>
/// Applications on this computer for the monitor point "Application" list: the launcher entries
/// (.desktop files) on Linux, the .app bundles on macOS, the registered programs on Windows.
/// </summary>
public static partial class InstalledProgramCatalog
{
    private const long MaxIconBytes = 512 * 1024;

    private static IReadOnlyList<InstalledAppInfo> _apps = [];

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static IReadOnlyList<InstalledAppInfo> Apps => _apps;

    public static InstalledAppInfo? Find(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return _apps.FirstOrDefault(app => PathComparer.Equals(app.ExecutablePath, path));
    }

    public static IReadOnlyList<InstalledAppInfo> Load()
    {
        var found = new Dictionary<string, InstalledAppInfo>(PathComparer);
        if (OperatingSystem.IsWindows())
        {
            ReadWindowsPrograms(found);
        }
        else if (OperatingSystem.IsMacOS())
        {
            ReadMacApps(found);
        }
        else if (OperatingSystem.IsLinux())
        {
            ReadDesktopEntries(found);
        }

        _apps = found.Values.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return _apps;
    }

    private static void ReadMacApps(Dictionary<string, InstalledAppInfo> found)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var root in new[] { "/Applications", "/Applications/Utilities", "/System/Applications", Path.Combine(home, "Applications") })
        {
            foreach (var bundle in SafeDirectories(root, "*.app"))
            {
                var name = Path.GetFileNameWithoutExtension(bundle);
                var info = ReadPlist(Path.Combine(bundle, "Contents", "Info.plist"));
                var executableName = info.GetValueOrDefault("CFBundleExecutable") ?? name;
                var executable = Path.Combine(bundle, "Contents", "MacOS", executableName);
                if (!File.Exists(executable))
                {
                    continue;
                }

                found.TryAdd(executable, new InstalledAppInfo
                {
                    Name = info.GetValueOrDefault("CFBundleDisplayName") ?? info.GetValueOrDefault("CFBundleName") ?? name,
                    ExecutablePath = executable,
                    ProcessName = executableName,
                    IconBase64 = MacIcon(bundle, info.GetValueOrDefault("CFBundleIconFile"))
                });
            }
        }
    }

    /// <summary>The string values of an XML Info.plist (binary plists are skipped; the bundle name is used then).</summary>
    private static Dictionary<string, string> ReadPlist(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var text = File.ReadAllText(path);
            if (!text.StartsWith("<?xml", StringComparison.Ordinal))
            {
                return values;
            }

            var matches = System.Text.RegularExpressions.Regex.Matches(text, @"<key>([^<]+)</key>\s*<string>([^<]*)</string>");
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                values.TryAdd(match.Groups[1].Value, System.Net.WebUtility.HtmlDecode(match.Groups[2].Value));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return values;
    }

    /// <summary>macOS icons are .icns, which the app cannot draw; a PNG next to it is used when the bundle has one.</summary>
    private static string? MacIcon(string bundle, string? iconFile)
    {
        var resources = Path.Combine(bundle, "Contents", "Resources");
        var baseName = string.IsNullOrWhiteSpace(iconFile) ? null : Path.GetFileNameWithoutExtension(iconFile);
        var candidates = baseName is null ? [] : new[] { Path.Combine(resources, baseName + ".png") };
        return candidates.Select(ReadIcon).FirstOrDefault(icon => icon is not null);
    }

    private static void ReadDesktopEntries(Dictionary<string, InstalledAppInfo> found)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdgHome
            ? xdgHome
            : Path.Combine(home, ".local", "share");
        var dataDirs = (Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } dirs ? dirs : "/usr/local/share:/usr/share")
            .Split(':', StringSplitOptions.RemoveEmptyEntries);
        var roots = new[] { dataHome }.Concat(dataDirs).Append("/var/lib/flatpak/exports/share").Distinct().ToList();

        foreach (var root in roots)
        {
            foreach (var file in SafeFiles(Path.Combine(root, "applications"), "*.desktop"))
            {
                if (ReadDesktopEntry(file, roots) is { } app)
                {
                    found.TryAdd(app.ExecutablePath, app);
                }
            }
        }
    }

    private static InstalledAppInfo? ReadDesktopEntry(string file, IReadOnlyList<string> roots)
    {
        Dictionary<string, string> entry;
        try
        {
            entry = ParseDesktopEntry(File.ReadLines(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (entry.GetValueOrDefault("Type") != "Application"
            || entry.GetValueOrDefault("NoDisplay") == "true"
            || entry.GetValueOrDefault("Hidden") == "true"
            || entry.GetValueOrDefault("Name") is not { Length: > 0 } name
            || entry.GetValueOrDefault("Exec") is not { Length: > 0 } exec
            || ResolveExecutable(exec) is not { } executable)
        {
            return null;
        }

        return new InstalledAppInfo
        {
            Name = name.Trim(),
            ExecutablePath = executable,
            ProcessName = Path.GetFileName(executable),
            IconBase64 = LinuxIcon(entry.GetValueOrDefault("Icon"), roots)
        };
    }

    private static Dictionary<string, string> ParseDesktopEntry(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var inMain = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inMain = line == "[Desktop Entry]";
                continue;
            }

            var equals = line.IndexOf('=');
            if (!inMain || equals <= 0 || line.StartsWith('#'))
            {
                continue;
            }

            values.TryAdd(line[..equals].Trim(), line[(equals + 1)..].Trim());
        }

        return values;
    }

    /// <summary>The program of an Exec line: its first word (without field codes or env), found on PATH when relative.</summary>
    private static string? ResolveExecutable(string exec)
    {
        var words = SplitCommand(exec).Where(word => !word.StartsWith('%')).ToList();
        if (words.Count > 0 && Path.GetFileName(words[0]) == "env")
        {
            words = words.Skip(1).SkipWhile(word => word.Contains('=') || word.StartsWith('-')).ToList();
        }

        if (words.Count == 0)
        {
            return null;
        }

        var program = words[0];
        if (Path.IsPathRooted(program))
        {
            return File.Exists(program) ? program : null;
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, program);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> SplitCommand(string command)
    {
        var word = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in command)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (word.Length > 0)
                {
                    yield return word.ToString();
                    word.Clear();
                }
            }
            else
            {
                word.Append(c);
            }
        }

        if (word.Length > 0)
        {
            yield return word.ToString();
        }
    }

    /// <summary>A PNG for the entry's icon: a path as given, else the hicolor theme or pixmaps.</summary>
    private static string? LinuxIcon(string? icon, IReadOnlyList<string> roots)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return null;
        }

        if (Path.IsPathRooted(icon))
        {
            return icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? ReadIcon(icon) : null;
        }

        foreach (var root in roots)
        {
            foreach (var size in new[] { "48x48", "64x64", "32x32", "128x128", "256x256" })
            {
                if (ReadIcon(Path.Combine(root, "icons", "hicolor", size, "apps", icon + ".png")) is { } found)
                {
                    return found;
                }
            }

            if (ReadIcon(Path.Combine(root, "pixmaps", icon + ".png")) is { } pixmap)
            {
                return pixmap;
            }
        }

        return null;
    }

    private static string? ReadIcon(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length is > 0 and <= MaxIconBytes ? Convert.ToBase64String(File.ReadAllBytes(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeDirectories(string root, string pattern)
    {
        try
        {
            return Directory.Exists(root) ? Directory.GetDirectories(root, pattern) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeFiles(string root, string pattern)
    {
        try
        {
            return Directory.Exists(root) ? Directory.GetFiles(root, pattern) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
