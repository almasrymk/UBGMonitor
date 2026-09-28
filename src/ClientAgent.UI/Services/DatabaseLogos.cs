using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using ClientAgent.Shared.Models;

namespace ClientAgent.UI.Services;

/// <summary>
/// Icon of the engine's management program installed on this device (SSMS, pgAdmin, MySQL Workbench),
/// falling back to a bundled logo. Returned as base64 so it flows through the same Icon binding as applications.
/// </summary>
public static class DatabaseLogos
{
    private const int IconSize = 64;

    private static readonly ConcurrentDictionary<DatabaseEngine, string?> Cache = new();

    private static readonly Dictionary<DatabaseEngine, string[]> Programs = new()
    {
        [DatabaseEngine.SqlServer] =
        [
            @"%ProgramFiles%\Microsoft SQL Server Management Studio *\Release\Common7\IDE\SSMS.exe",
            @"%ProgramFiles%\Microsoft SQL Server Management Studio *\Common7\IDE\SSMS.exe",
            @"%ProgramFiles(x86)%\Microsoft SQL Server Management Studio *\Common7\IDE\Ssms.exe",
            @"%ProgramFiles(x86)%\Microsoft SQL Server\*\Tools\Binn\ManagementStudio\Ssms.exe",
            @"%LocalAppData%\Programs\Azure Data Studio\azuredatastudio.exe",
            @"%ProgramFiles%\Azure Data Studio\azuredatastudio.exe"
        ],
        [DatabaseEngine.PostgreSql] =
        [
            @"%ProgramFiles%\pgAdmin 4\runtime\pgAdmin4.exe",
            @"%LocalAppData%\Programs\pgAdmin 4\runtime\pgAdmin4.exe",
            @"%ProgramFiles%\PostgreSQL\*\pgAdmin 4\runtime\pgAdmin4.exe",
            @"%ProgramFiles%\PostgreSQL\*\pgAdmin 4\bin\pgAdmin4.exe"
        ],
        [DatabaseEngine.MySql] =
        [
            @"%ProgramFiles%\MySQL\MySQL Workbench *\MySQLWorkbench.exe",
            @"%ProgramFiles(x86)%\MySQL\MySQL Workbench *\MySQLWorkbench.exe"
        ]
    };

    public static string? Base64(DatabaseEngine? engine)
        => engine is DatabaseEngine value ? Cache.GetOrAdd(value, Load) : null;

    private static string? Load(DatabaseEngine engine)
    {
        foreach (var pattern in Programs[engine])
        {
            foreach (var path in Expand(Environment.ExpandEnvironmentVariables(pattern)))
            {
                if (ReadIcon(path) is string icon)
                {
                    return icon;
                }
            }
        }

        return Bundled(engine);
    }

    /// <summary>Expands '*' directory segments, newest (highest-named) folder first.</summary>
    private static IEnumerable<string> Expand(string pattern)
    {
        var star = pattern.IndexOf('*');
        if (star < 0)
        {
            return File.Exists(pattern) ? [pattern] : [];
        }

        var parentEnd = pattern.LastIndexOf('\\', star);
        var segmentEnd = pattern.IndexOf('\\', star);
        if (parentEnd < 0 || segmentEnd < 0)
        {
            return [];
        }

        var parent = pattern[..parentEnd];
        var segment = pattern[(parentEnd + 1)..segmentEnd];
        var rest = pattern[segmentEnd..];
        if (!Directory.Exists(parent))
        {
            return [];
        }

        try
        {
            return Directory.GetDirectories(parent, segment)
                .OrderByDescending(dir => dir, StringComparer.OrdinalIgnoreCase)
                .SelectMany(dir => Expand(dir + rest))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

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
