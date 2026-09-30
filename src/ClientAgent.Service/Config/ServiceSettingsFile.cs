using System.Text.Json;
using System.Text.Json.Nodes;
using ClientAgent.Shared.Security;

namespace ClientAgent.Service.Config;

/// <summary>
/// Reads and writes the "Setting" section of the service's own appsettings.json, which the app edits
/// through the local API. <see cref="LocalConfigCache"/> picks up the change from the file time stamp.
/// </summary>
public static class ServiceSettingsFile
{
    private const string SectionName = "Setting";
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly object Gate = new();

    public static string PrimaryPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static JsonObject ReadSection()
    {
        lock (Gate)
        {
            if (!File.Exists(PrimaryPath))
            {
                return [];
            }

            var root = JsonNode.Parse(File.ReadAllText(PrimaryPath)) as JsonObject;
            var section = root?[SectionName] ?? root?["Ui"];
            return section?.DeepClone() as JsonObject ?? [];
        }
    }

    public static void WriteSection(JsonObject section)
    {
        ProtectPasswords(section);
        lock (Gate)
        {
            Write(PrimaryPath, section);
            foreach (var copy in DevelopmentCopies())
            {
                try
                {
                    Write(copy, section);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Source / bin copies only keep settings across rebuilds while developing.
                }
            }
        }
    }

    /// <summary>
    /// Encrypts database passwords with this computer's key. The app sends a newly typed password as plain text,
    /// because an app on another computer cannot encrypt it with this computer's key.
    /// </summary>
    private static void ProtectPasswords(JsonObject section)
    {
        foreach (var database in (section["MonitorPoints"] as JsonArray ?? []).OfType<JsonObject>().Select(point => point["Database"]).OfType<JsonObject>())
        {
            if (database["Password"]?.GetValue<string>() is { Length: > 0 } password)
            {
                database["Password"] = SecretProtector.Protect(password);
            }
        }
    }

    private static void Write(string path, JsonObject section)
    {
        var root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [] : [];
        root[SectionName] = section.DeepClone();
        root.Remove("Ui");
        root.Remove("MonitorPoints");
        File.WriteAllText(path, root.ToJsonString(WriteOptions));
    }

    /// <summary>When the service runs from a build folder, the project's appsettings.json (and its other build outputs) are updated too.</summary>
    private static IEnumerable<string> DevelopmentCopies()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var source = Path.Combine(dir.FullName, "src", "ClientAgent.Service", "appsettings.json");
            if (!File.Exists(source))
            {
                continue;
            }

            var projectDir = Path.GetDirectoryName(source)!;
            var candidates = new[] { source }
                .Concat(new[] { "Debug", "Release" }.Select(configuration =>
                    Path.Combine(projectDir, "bin", configuration, "net8.0-windows", "appsettings.json")));
            foreach (var path in candidates.Where(File.Exists))
            {
                if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(PrimaryPath), StringComparison.OrdinalIgnoreCase))
                {
                    yield return path;
                }
            }

            yield break;
        }
    }
}
