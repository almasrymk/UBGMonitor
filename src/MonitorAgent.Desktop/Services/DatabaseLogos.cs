using System.Collections.Concurrent;
using Avalonia.Platform;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.UI.Services;

/// <summary>The engine's bundled logo, as base64 so it flows through the same Icon binding as applications.</summary>
public static class DatabaseLogos
{
    private static readonly ConcurrentDictionary<DatabaseEngine, string?> Cache = new();

    public static string? Base64(DatabaseEngine? engine)
        => engine is DatabaseEngine value ? Cache.GetOrAdd(value, Bundled) : null;

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
