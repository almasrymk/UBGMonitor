using System.Collections.Concurrent;
using Avalonia.Platform;
using MonitorAgent.Desktop.Services;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.UI.Services;

/// <summary>
/// On Windows the icon of the engine's management program installed on this device (SSMS, pgAdmin, MySQL
/// Workbench), as the WPF app shows; otherwise, or when none is installed, a bundled logo. Returned as base64 so
/// it flows through the same Icon binding as applications.
/// </summary>
public static partial class DatabaseLogos
{
    private static readonly ConcurrentDictionary<DatabaseEngine, string?> Cache = new();

    public static string? Base64(DatabaseEngine? engine)
        => engine is DatabaseEngine value ? Cache.GetOrAdd(value, Load) : null;

    private static string? Load(DatabaseEngine engine)
        => (OperatingSystem.IsWindows() ? InstalledPrograms(engine).Select(ReadIcon).FirstOrDefault(icon => icon is not null) : null)
           ?? Bundled(engine);

    private static string? ReadIcon(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using var icon = WindowsIcons.Extract(path, IconSize) ?? WindowsIcons.Associated(path);
        if (icon is null)
        {
            return null;
        }

        using var stream = new MemoryStream();
        icon.Save(stream);
        return Convert.ToBase64String(stream.ToArray());
    }

    private static string? Bundled(DatabaseEngine engine)
    {
        var file = engine switch
        {
            DatabaseEngine.PostgreSql => "postgresql.png",
            DatabaseEngine.MySql => "mysql.png",
            _ => "sqlserver.png"
        };

        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://MonitorAgent/Assets/Databases/{file}"));
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return Convert.ToBase64String(buffer.ToArray());
        }
        catch (Exception ex) when (ex is IOException or FileNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
