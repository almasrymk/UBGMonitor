namespace ClientAgent.Shared.Models;

public sealed class MonitoringEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public string AgentId { get; init; } = string.Empty;

    public string MonitorPointId { get; init; } = string.Empty;

    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    public EventType EventType { get; init; }

    public Severity Severity { get; init; }

    public EventStatus Status { get; init; }

    public double? MeasuredValue { get; init; }

    public double? Threshold { get; init; }

    public string Message { get; init; } = string.Empty;

    public Dictionary<string, string> Metadata { get; init; } = new();

    public string ConfigVersion { get; init; } = "0";

    public string AgentVersion { get; init; } = "1.0.0";
}
