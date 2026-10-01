using System.Collections.Concurrent;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Monitoring;

public interface IMonitorHealthStore
{
    /// <param name="responseMs">How long the check took to get an answer, when the point answered.</param>
    void SetPointHealth(string monitorPointId, bool isUp, string? message = null, string? status = null, double? responseMs = null);

    bool? GetIsUp(string monitorPointId);

    double? GetResponseMs(string monitorPointId);

    /// <summary>When the point's status last changed (UTC).</summary>
    DateTime? GetStatusSinceUtc(string monitorPointId);

    string? GetStatus(string monitorPointId);

    DateTime? GetLastCheckedUtc(string monitorPointId);

    string? GetMessage(string monitorPointId);

    void SetIssue(string key, string severity, string title, string message, string? monitorPointId = null);

    void ClearIssue(string key);

    IReadOnlyList<AgentIssueDto> GetIssues();
}

public sealed class MonitorHealthStore : IMonitorHealthStore
{
    private readonly ConcurrentDictionary<string, PointHealth> _points = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AgentIssueDto> _issues = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceProvider _services;

    public MonitorHealthStore(IServiceProvider services)
    {
        _services = services;
    }

    public void SetPointHealth(string monitorPointId, bool isUp, string? message = null, string? status = null, double? responseMs = null)
    {
        var resolved = string.IsNullOrWhiteSpace(status) ? (isUp ? "Healthy" : "Critical") : status;
        var previous = _points.TryGetValue(monitorPointId, out var old) ? old : null;
        var changed = previous is null || previous.IsUp != isUp || previous.Status != resolved;
        var now = DateTime.UtcNow;
        _points[monitorPointId] = new PointHealth(isUp, now, message, resolved, responseMs, changed ? now : previous!.StatusSinceUtc);
        if (changed)
        {
            _services.GetRequiredService<NotificationTrigger>().Request();
        }
    }

    public bool? GetIsUp(string monitorPointId)
        => _points.TryGetValue(monitorPointId, out var health) ? health.IsUp : null;

    public double? GetResponseMs(string monitorPointId)
        => _points.TryGetValue(monitorPointId, out var health) ? health.ResponseMs : null;

    public DateTime? GetStatusSinceUtc(string monitorPointId)
        => _points.TryGetValue(monitorPointId, out var health) ? health.StatusSinceUtc : null;

    public string? GetStatus(string monitorPointId)
        => _points.TryGetValue(monitorPointId, out var health) ? health.Status : null;

    public DateTime? GetLastCheckedUtc(string monitorPointId)
        => _points.TryGetValue(monitorPointId, out var health) ? health.LastCheckedUtc : null;

    public string? GetMessage(string monitorPointId)
        => _points.TryGetValue(monitorPointId, out var health) ? health.Message : null;

    /// <summary>Called by a monitor each time its check fails or warns; every call is also saved to the Data file.</summary>
    public void SetIssue(string key, string severity, string title, string message, string? monitorPointId = null)
    {
        var changed = !_issues.TryGetValue(key, out var before) || before.Severity != severity;
        var issue = _issues.AddOrUpdate(
            key,
            _ => new AgentIssueDto
            {
                Id = key,
                Severity = severity,
                Title = title,
                Message = message,
                TimestampUtc = DateTime.UtcNow,
                MonitorPointId = monitorPointId
            },
            (_, existing) => new AgentIssueDto
            {
                Id = existing.Id,
                Severity = severity,
                Title = title,
                Message = message,
                TimestampUtc = existing.TimestampUtc,
                MonitorPointId = monitorPointId ?? existing.MonitorPointId
            });

        _services.GetRequiredService<IIssueDataLogger>().Record(CheckName(issue.MonitorPointId), [issue]);
        if (changed)
        {
            _services.GetRequiredService<NotificationTrigger>().Request();
        }
    }

    public void ClearIssue(string key)
    {
        if (_issues.TryRemove(key, out var issue))
        {
            _services.GetRequiredService<IIssueDataLogger>().RecordResolved(CheckName(issue.MonitorPointId), [issue]);
            _services.GetRequiredService<NotificationTrigger>().Request();
        }
    }

    private static string CheckName(string? monitorPointId) =>
        monitorPointId is null ? "Service check" : $"Service check: {monitorPointId}";

    public IReadOnlyList<AgentIssueDto> GetIssues()
        => _issues.Values.OrderByDescending(i => i.TimestampUtc).ToList();

    private sealed record PointHealth(bool IsUp, DateTime LastCheckedUtc, string? Message, string Status, double? ResponseMs, DateTime StatusSinceUtc);
}
