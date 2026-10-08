using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using MonitorCloud.AgentProtocol.V1;

namespace MonitorAgent.Cloud;

/// <summary>
/// Compares the agent's active problems with what the cloud was last told (kept in the outbox database, so a restart
/// still clears problems that ended while the agent was down) and produces the <see cref="IssueEvent"/>s.
/// </summary>
public sealed class IssueChangeTracker(Outbox outbox)
{
    private const string StateKey = "issues";

    /// <summary>Keys the cloud owns; an agent problem with one of these names is not reported.</summary>
    private static readonly HashSet<string> CloudKeys = new(StringComparer.OrdinalIgnoreCase) { "device-offline", "license", "clock-skew", "clone-suspected" };

    public IReadOnlyList<IssueEvent> Changes(IReadOnlyList<CloudIssue> current, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(current);
        var sent = Load();
        var events = new List<IssueEvent>();
        var active = current.Where(i => !CloudKeys.Contains(i.Key)).GroupBy(i => Key(i.Key), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var (key, issue) in active)
        {
            var severity = SeverityOf(issue.Severity);
            if (!sent.TryGetValue(key, out var previous))
                events.Add(Event(key, IssueAction.Raised, severity, issue, issue.At));
            else if (previous != (int)severity)
                events.Add(Event(key, IssueAction.SeverityChanged, severity, issue, now));
            sent[key] = (int)severity;
        }

        foreach (var key in sent.Keys.Where(k => !active.ContainsKey(k)).ToList())
        {
            events.Add(new IssueEvent { IssueKey = key, Action = IssueAction.Cleared, Severity = (Severity)sent[key], OccurredAt = Timestamp.FromDateTimeOffset(now) });
            sent.Remove(key);
        }

        if (events.Count > 0)
            outbox.SetState(StateKey, JsonSerializer.Serialize(sent));
        return events;
    }

    public static Severity SeverityOf(string? severity) => severity?.ToLowerInvariant() switch
    {
        "critical" or "problem" or "error" => Severity.Critical,
        "warning" => Severity.Warning,
        _ => Severity.Info,
    };

    /// <summary>The cloud category (02 section 6) from the agent's key conventions.</summary>
    public static string CategoryOf(string key, string? monitorPointKey)
    {
        var k = key.ToLowerInvariant();
        if (k is "cpu" or "ram" || k.StartsWith("cpu", StringComparison.Ordinal) || k.StartsWith("ram", StringComparison.Ordinal))
            return "Performance";
        if (k.StartsWith("disk", StringComparison.Ordinal))
            return "Storage";
        if (k.StartsWith("internet", StringComparison.Ordinal) || k.StartsWith("network", StringComparison.Ordinal) || k.StartsWith("ping", StringComparison.Ordinal))
            return "Connectivity";
        if (k.StartsWith("db", StringComparison.Ordinal) || k.StartsWith("database", StringComparison.Ordinal))
            return "Database";
        if (k.StartsWith("app", StringComparison.Ordinal))
            return "Application";
        return monitorPointKey is null ? "System" : "Service";
    }

    private static string Key(string key) => key.Length <= 128 ? key : key[..128];

    private static IssueEvent Event(string key, IssueAction action, Severity severity, CloudIssue issue, DateTimeOffset at) => new()
    {
        IssueKey = key,
        Action = action,
        Severity = severity,
        Category = CategoryOf(issue.Key, issue.MonitorPointKey),
        Title = Truncate(issue.Title, 200),
        Message = Truncate(issue.Message, 2000),
        OccurredAt = Timestamp.FromDateTimeOffset(at),
        MonitorPointKey = issue.MonitorPointKey ?? string.Empty,
    };

    private Dictionary<string, int> Load()
    {
        try
        {
            return outbox.GetState(StateKey) is { } json
                ? JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new Dictionary<string, int>(StringComparer.Ordinal)
                : new Dictionary<string, int>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }

    private static string Truncate(string? value, int max) => value is null ? string.Empty : value.Length <= max ? value : value[..max];
}
