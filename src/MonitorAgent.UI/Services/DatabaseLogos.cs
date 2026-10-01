using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.UI.Services;

/// <summary>
/// Icon of the engine's management program installed on this device (SSMS, pgAdmin, MySQL Workbench),
/// falling back to a bundled logo. Returned as base64 so it flows through the same Icon binding as applications.
/// </summary>
public static partial class DatabaseLogos
{
    private static readonly ConcurrentDictionary<DatabaseEngine, string?> Cache = new();

    public static string? Base64(DatabaseEngine? engine)
        => engine is DatabaseEngine value ? Cache.GetOrAdd(value, Load) : null;

    private static string? Load(DatabaseEngine engine)
        => InstalledPrograms(engine).Select(ReadIcon).FirstOrDefault(icon => icon is not null) ?? Bundled(engine);

    private static string? ReadIcon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractIcon(path, 0, IconSize)
                             ?? System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return Convert.ToBase64String(stream.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
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
            var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Databases/{file}"));
            if (resource is null)
            {
                return null;
            }

            using var stream = resource.Stream;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return Convert.ToBase64String(buffer.ToArray());
        }
        catch (IOException)
        {
            return null;
        }
    }
}
