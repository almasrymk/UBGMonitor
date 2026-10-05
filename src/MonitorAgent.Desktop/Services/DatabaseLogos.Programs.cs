using System.IO;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.UI.Services;

public static partial class DatabaseLogos
{
    private const int IconSize = 64;

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

    /// <summary>The best installed management program for the engine, or null.</summary>
    public static string? FindProgram(DatabaseEngine engine) => InstalledPrograms(engine).FirstOrDefault();

    /// <summary>Installed management programs for the engine, best match first.</summary>
    private static IEnumerable<string> InstalledPrograms(DatabaseEngine engine)
        => Programs[engine].SelectMany(pattern => Expand(Environment.ExpandEnvironmentVariables(pattern)));

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
}
