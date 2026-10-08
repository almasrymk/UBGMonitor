using System.IO;
using System.Text.Json;

namespace MonitorAgent.UI.Services;

/// <summary>Consent belongs to the desktop user, never to service-supplied settings.</summary>
public static class ApplicationLaunchApprovals
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorAgent", "application-approvals.json");
    public static bool IsApproved(string path) => Read().Contains(Path.GetFullPath(path), OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    public static void Approve(string path)
    {
        var paths = Read();
        paths.Add(Path.GetFullPath(path));
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options)) JsonSerializer.Serialize(stream, paths);
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static List<string> Read()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
}
