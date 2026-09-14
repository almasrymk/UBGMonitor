namespace ClientAgent.Shared.Models;

public sealed class MonitorPoint
{
    public string MonitorPointId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public MonitorPointType Type { get; init; }

    public string Address { get; init; } = string.Empty;

    public string Location { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public int IntervalSeconds { get; init; } = 30;

    public double? WarningThreshold { get; init; }

    public double? CriticalThreshold { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new();
}
