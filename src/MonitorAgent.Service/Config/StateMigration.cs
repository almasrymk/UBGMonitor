using System.Text.Json.Nodes;
using System.Security.Cryptography;
using MonitorAgent.Shared.Security;

namespace MonitorAgent.Service.Config;

public static class StateMigration
{
    public static void Run(string state, string install, bool windowsLayout)
    {
        PrivateFile.EnsureDirectory(state);
        var target = Path.Combine(state, "settings.json");
        var backup = Path.Combine(state, "backups");
        var candidates = new[] { Path.Combine(state, "appsettings.previous.json"), Path.Combine(state, "publish", "appsettings.json"), Path.Combine(install, "appsettings.json") };
        if (!File.Exists(target))
        {
            foreach (var source in candidates.Distinct())
            {
                if (!File.Exists(source)) continue;
                if (new FileInfo(source).LinkTarget is not null) throw new IOException("Legacy settings cannot be symbolic links.");
                var root = JsonNode.Parse(File.ReadAllText(source)) as JsonObject ?? throw new IOException("Legacy settings are invalid.");
                var settings = root["Setting"] ?? root["Ui"];
                if (settings is null && root["MonitorPoints"] is not null) settings = new JsonObject { ["MonitorPoints"] = root["MonitorPoints"]!.DeepClone() };
                if (settings is null) continue;
                PrivateFile.Backup(source, backup);
                PrivateFile.WriteAllText(target, settings.ToJsonString());
                break;
            }
        }
        if (File.Exists(target)) PrivateFile.Secure(target);
        if (windowsLayout)
            foreach (var name in new[] { "Data", "Reports" }) MoveTree(Path.Combine(state, "publish", name), Path.Combine(state, name), backup);
        // A successful marker permits the installer to remove obsolete binaries only after startup.
        if (!File.Exists(Path.Combine(state, "layout-migrated"))) PrivateFile.WriteAllText(Path.Combine(state, "layout-migrated"), "1");
    }

    private static void MoveTree(string source, string destination, string backups)
    {
        if (!Directory.Exists(source)) return;
        if (new DirectoryInfo(source).LinkTarget is not null) throw new IOException("Legacy data cannot be symbolic links.");
        PrivateFile.EnsureDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (new FileInfo(file).LinkTarget is not null) throw new IOException("Legacy data cannot contain symbolic links.");
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (File.Exists(target) && !SameContents(target, file))
                throw new IOException("Existing state conflicts with legacy data; both copies have been preserved.");
            PrivateFile.Backup(file, backups);
            if (!File.Exists(target)) PrivateFile.Copy(file, target);
            PrivateFile.Secure(target);
            File.Delete(file);
        }
        foreach (var directory in Directory.EnumerateDirectories(source)) MoveTree(directory, Path.Combine(destination, Path.GetFileName(directory)), backups);
        Directory.Delete(source);
    }
    private static bool SameContents(string left, string right)
    {
        using var a = File.OpenRead(left);
        using var b = File.OpenRead(right);
        return a.Length == b.Length && SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b));
    }
}
