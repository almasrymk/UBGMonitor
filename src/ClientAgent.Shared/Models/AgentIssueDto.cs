namespace ClientAgent.Shared.Models;

public sealed class AgentIssueDto
{
    public string Id { get; init; } = string.Empty;

    public string Severity { get; init; } = "Warning";

    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    public string? MonitorPointId { get; init; }
}
