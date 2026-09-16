using System.Collections.Concurrent;
using ClientAgent.Shared.Models;

namespace ClientAgent.Service.Monitoring;

public interface IMonitorHealthStore
{
    void SetPointHealth(string monitorPointId, bool isUp, string? message = null);

    bool? GetIsUp(string monitorPointId);

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

    public void SetPointHealth(string monitorPointId, bool isUp, string? message = null)
    {
        _points[monitorPointId] = new PointHealth(isUp, DateTime.UtcNow, message);
    }

    public bool? GetIsUp(string monitorPointId)
        => _points.TryGetValue(monitorPointId, out var health) ? health.IsUp : null;

    public DateTime? GetLastCheckedUtc(string monitorPointId)
        => _points.TryGetValue(monitorPointId, out var health) ? health.LastCheckedUtc : null;

    public string? GetMessage(string monitorPointId)
        => _points.TryGetValue(monitorPointId, out var health) ? health.Message : null;

    public void SetIssue(string key, string severity, string title, string message, string? monitorPointId = null)
    {
        _issues.AddOrUpdate(
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
    }

    public void ClearIssue(string key) => _issues.TryRemove(key, out _);

    public IReadOnlyList<AgentIssueDto> GetIssues()
        => _issues.Values.OrderByDescending(i => i.TimestampUtc).ToList();

    private sealed record PointHealth(bool IsUp, DateTime LastCheckedUtc, string? Message);
}
