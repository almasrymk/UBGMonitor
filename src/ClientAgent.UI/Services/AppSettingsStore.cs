using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClientAgent.Shared.Models;
using ClientAgent.UI.Models;
using Microsoft.Win32;

namespace ClientAgent.UI.Services;

public sealed class AppSettingsStore
{
    private const string SectionName = "Setting";
    private const string ServiceName = "ClientAgentService";
    private const string ServiceProcessName = "ClientAgent.Service";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IReadOnlyList<string> _paths;

    public AppSettingsStore(string? path = null)
    {
        _paths = path is null ? ResolveServicePaths() : [path];
    }

    public string FilePath => _paths.FirstOrDefault() ?? string.Empty;

    public IReadOnlyList<MonitorPoint> LoadMonitorPoints()
    {
        var path = _paths.FirstOrDefault(File.Exists);
        if (path is null)
        {
            return [];
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            var nested = root?["Setting"]?["MonitorPoints"] ?? root?["Ui"]?["MonitorPoints"] ?? root?["MonitorPoints"];
            return nested?.Deserialize<List<MonitorPoint>>(JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public UiAppSettings Load()
    {
        var path = _paths.FirstOrDefault(File.Exists);
        if (path is null)
        {
            return new UiAppSettings();
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            var section = root?["Setting"] ?? root?["Ui"];
            var settings = section?.Deserialize<UiAppSettings>(JsonOptions) ?? new UiAppSettings();
            if (section is JsonObject obj && obj["General"] is null && obj["RefreshInterval"] is not null)
            {
                settings.General = obj.Deserialize<GeneralSettings>(JsonOptions) ?? settings.General;
            }

            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new UiAppSettings();
        }
    }

    public void Save(UiAppSettings settings, IReadOnlyList<MonitorPoint> monitorPoints)
    {
        if (_paths.Count == 0)
        {
            throw new IOException("Service appsettings.json was not found.");
        }

        settings.MonitorPoints = monitorPoints.ToList();
        var section = JsonSerializer.SerializeToNode(settings, JsonOptions);
        var primary = _paths[0];
        foreach (var path in _paths)
        {
            try
            {
                WriteSection(path, section);
            }
            catch (Exception ex) when (path != primary && ex is IOException or UnauthorizedAccessException)
            {
                // Secondary copies (source/bin) are only for development.
            }
        }
    }

    private static void WriteSection(string path, JsonNode? section)
    {
        JsonObject root;
        if (File.Exists(path))
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
        }
        else
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            root = new JsonObject();
        }

        root[SectionName] = section?.DeepClone();
        root.Remove("Ui");
        root.Remove("MonitorPoints");
        File.WriteAllText(path, root.ToJsonString(JsonOptions));
    }

    /// <summary>
    /// The file the installed/running service actually reads comes first, so Load() shows what the
    /// service uses; source and bin copies are kept in sync for development.
    /// </summary>
    private static IReadOnlyList<string> ResolveServicePaths()
    {
        var paths = new List<string>();
        foreach (var directory in ServiceExecutableDirectories())
        {
            var file = Path.Combine(directory, "appsettings.json");
            if (File.Exists(file) && !paths.Contains(file, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(file);
            }
        }

        var source = FindServiceAppSettings();
        if (source is not null && !paths.Contains(source, StringComparer.OrdinalIgnoreCase))
        {
            paths.Add(source);
            var projectDir = Path.GetDirectoryName(source);
            if (!string.IsNullOrEmpty(projectDir))
            {
                foreach (var configuration in new[] { "Debug", "Release" })
                {
                    var binPath = Path.Combine(projectDir, "bin", configuration, "net8.0-windows", "appsettings.json");
                    if (File.Exists(binPath) && !paths.Contains(binPath, StringComparer.OrdinalIgnoreCase))
                    {
                        paths.Add(binPath);
                    }
                }
            }
        }

        const string published = @"C:\ProgramData\ClientAgent\publish\appsettings.json";
        if (File.Exists(published) && !paths.Contains(published, StringComparer.OrdinalIgnoreCase))
        {
            paths.Add(published);
        }

        return paths;
    }

    private static IEnumerable<string> ServiceExecutableDirectories()
    {
        foreach (var process in Process.GetProcessesByName(ServiceProcessName))
        {
            using (process)
            {
                if (ProcessImagePath(process) is string running)
                {
                    yield return Path.GetDirectoryName(running)!;
                }
            }
        }

        if (RegisteredServicePath() is string registered)
        {
            yield return Path.GetDirectoryName(registered)!;
        }

        if (File.Exists(Path.Combine(AppContext.BaseDirectory, ServiceProcessName + ".exe")))
        {
            yield return AppContext.BaseDirectory;
        }
    }

    private static string? RegisteredServicePath()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
            if (key?.GetValue("ImagePath") is not string imagePath)
            {
                return null;
            }

            var text = Environment.ExpandEnvironmentVariables(imagePath.Trim());
            var exe = text.StartsWith('"')
                ? text[1..Math.Max(1, text.IndexOf('"', 1))]
                : text[..(text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) is var end and >= 0 ? end + 4 : text.Length)];
            return File.Exists(exe) ? exe : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>The service runs as SYSTEM, so MainModule is denied; limited query still returns the image path.</summary>
    private static string? ProcessImagePath(Process process)
    {
        const uint queryLimitedInformation = 0x1000;
        var handle = OpenProcess(queryLimitedInformation, false, process.Id);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private static string? FindServiceAppSettings()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var underSrc = Path.Combine(dir.FullName, "src", "ClientAgent.Service", "appsettings.json");
            if (File.Exists(underSrc))
            {
                return underSrc;
            }

            var beside = Path.Combine(dir.FullName, "ClientAgent.Service", "appsettings.json");
            if (File.Exists(beside))
            {
                return beside;
            }
        }

        return null;
    }
}
