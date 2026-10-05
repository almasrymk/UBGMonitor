using System.Globalization;
using System.Text.RegularExpressions;

namespace MonitorAgent.Service.Platform;

/// <summary>Sign-ins on Linux and macOS from the who and last tools, which both systems have.</summary>
public static partial class UnixLogins
{
    public sealed record WhoEntry(string UserName, string Line, string? Host, DateTime? SignedInUtc);

    public sealed record LastLogon(DateTime LatestUtc, int Count);

    /// <summary>Who is signed in now, one entry per terminal or desktop.</summary>
    public static List<WhoEntry> Who()
    {
        var entries = new List<WhoEntry>();
        if (Command.Run("who", string.Empty) is not { Succeeded: true } result)
        {
            return entries;
        }

        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2)
            {
                continue;
            }

            var host = HostPattern().Match(line) is { Success: true } match ? match.Groups[1].Value : null;
            entries.Add(new WhoEntry(words[0], words[1], string.IsNullOrWhiteSpace(host) || host.StartsWith(':') ? null : host, ParseDate(line)));
        }

        return entries;
    }

    /// <summary>The latest sign-in and the number of sign-ins of each user that the system log still holds.</summary>
    public static Dictionary<string, LastLogon> Last(string arguments)
    {
        var logons = new Dictionary<string, LastLogon>(StringComparer.Ordinal);
        if (Command.Run("last", arguments, 15_000) is not { Succeeded: true } result)
        {
            return logons;
        }

        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var user = line.Split(' ', 2)[0];
            if (user is "reboot" or "shutdown" or "wtmp" or "wtmpdb" or "btmp" || ParseDate(line) is not { } date)
            {
                continue;
            }

            // last lists the newest first.
            logons[user] = logons.TryGetValue(user, out var seen) ? seen with { Count = seen.Count + 1 } : new LastLogon(date, 1);
        }

        return logons;
    }

    /// <summary>The first date in a line: "2026-10-05 12:00" (GNU who) or "Mon Oct  5 12:00[:00] [2026]" (last, BSD who).</summary>
    public static DateTime? ParseDate(string text)
    {
        if (IsoPattern().Match(text) is { Success: true } iso
            && DateTime.TryParse($"{iso.Groups[1].Value} {iso.Groups[2].Value}", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var isoDate))
        {
            return isoDate.ToUniversalTime();
        }

        if (MonthPattern().Match(text) is not { Success: true } match)
        {
            return null;
        }

        var hasYear = match.Groups[4].Success;
        var year = hasYear ? int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) : DateTime.Now.Year;
        var time = match.Groups[3].Value.Length == 5 ? match.Groups[3].Value + ":00" : match.Groups[3].Value;
        if (!DateTime.TryParseExact($"{match.Groups[1].Value} {match.Groups[2].Value} {year} {time}", "MMM d yyyy HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
        {
            return null;
        }

        // Without a year, a date later than today was last year.
        if (!hasYear && date > DateTime.Now.AddDays(1))
        {
            date = date.AddYears(-1);
        }

        return date.ToUniversalTime();
    }

    [GeneratedRegex(@"\(([^)]*)\)\s*$")]
    private static partial Regex HostPattern();

    [GeneratedRegex(@"(\d{4}-\d{2}-\d{2})[ T](\d{2}:\d{2}(?::\d{2})?)")]
    private static partial Regex IsoPattern();

    [GeneratedRegex(@"\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) +(\d{1,2}) (\d{2}:\d{2}(?::\d{2})?)(?: (\d{4}))?")]
    private static partial Regex MonthPattern();
}
