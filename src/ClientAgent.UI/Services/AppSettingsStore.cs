using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClientAgent.Shared.Models;
using ClientAgent.UI.Models;

namespace ClientAgent.UI.Services;

public sealed class AppSettingsStore
{
    private const string SectionName = "Setting";

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
        foreach (var path in _paths)
        {
            WriteSection(path, section);
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

    private static IReadOnlyList<string> ResolveServicePaths()
    {
        var paths = new List<string>();
        var source = FindServiceAppSettings();
        if (source is not null)
        {
            paths.Add(source);
            var projectDir = Path.GetDirectoryName(source);
            if (!string.IsNullOrEmpty(projectDir))
            {
                foreach (var configuration in new[] { "Debug", "Release" })
                {
                    var binPath = Path.Combine(projectDir, "bin", configuration, "net8.0-windows", "appsettings.json");
                    if (File.Exists(binPath))
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
