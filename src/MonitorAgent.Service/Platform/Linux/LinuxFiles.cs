using System.Globalization;

namespace MonitorAgent.Service.Platform.Linux;

/// <summary>Reads the small text files of /proc and /sys; a missing or unreadable file is just "no value".</summary>
internal static class LinuxFiles
{
    public static string Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    public static string? ReadLine(string path)
    {
        var text = Read(path).Trim();
        return text.Length == 0 ? null : text;
    }

    public static long? ReadLong(string path)
        => long.TryParse(ReadLine(path), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    public static IEnumerable<string> Directories(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetDirectories(path).Order(StringComparer.Ordinal) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static IEnumerable<string> Files(string path, string pattern)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetFiles(path, pattern).Order(StringComparer.Ordinal) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
