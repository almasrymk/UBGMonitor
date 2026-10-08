using System.Text.Json;
using System.Text.Json.Nodes;
using MonitorAgent.Shared.Security;
using MonitorAgent.Service.Platform;

namespace MonitorAgent.Service.Config;

/// <summary>
/// Reads and writes the "Setting" section of the service's own appsettings.json, which the app edits
/// through the local API. <see cref="LocalConfigCache"/> picks up the change from the file time stamp.
/// </summary>
public static class ServiceSettingsFile
{
    private const string SectionName = "Setting";
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly object Gate = new();

    public static string PrimaryPath => AgentPaths.SettingsPath;

    public static JsonObject ReadSection()
    {
        lock (Gate)
        {
            if (!File.Exists(PrimaryPath))
            {
                return [];
            }

            var root = JsonNode.Parse(File.ReadAllText(PrimaryPath)) as JsonObject;
            var section = root?[SectionName] ?? root?["Ui"] ?? root;
            var result = section?.DeepClone() as JsonObject ?? [];
            var changed = false;
            foreach (var database in (result["MonitorPoints"] as JsonArray ?? []).OfType<JsonObject>().Select(p => p["Database"]).OfType<JsonObject>())
                if (database["Password"]?.GetValue<string>() is { } password && SecretProtector.IsLegacy(password))
                {
                    var replacement = SecretProtector.ReprotectLegacy(password);
                    if (replacement != password) { database["Password"] = replacement; changed = true; }
                }
            if (changed)
            {
                PrivateFile.Backup(PrimaryPath, Path.Combine(AgentPaths.StateFolder, "backups"));
                Write(PrimaryPath, result);
            }
            return result;
        }
    }

    public static void WriteSection(JsonObject section)
    {
        ProtectPasswords(section);
        lock (Gate)
        {
            Write(PrimaryPath, section);
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
        PrivateFile.WriteAllText(path, section.ToJsonString(WriteOptions));
    }

#if DEBUG
    /// <summary>When the service runs from a build folder, the project's appsettings.json (and its other build outputs) are updated too.</summary>
    private static IEnumerable<string> DevelopmentCopies()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var source = Path.Combine(dir.FullName, "src", "MonitorAgent.Service", "appsettings.json");
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
#endif
}
