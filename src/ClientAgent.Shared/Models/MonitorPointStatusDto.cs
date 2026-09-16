namespace ClientAgent.Shared.Models;

public sealed class MonitorPointStatusDto
{
    public string MonitorPointId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public MonitorPointType Type { get; init; }

    public string Address { get; init; } = string.Empty;

    public string Location { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public int IntervalSeconds { get; init; }

    public bool? IsUp { get; init; }

    public string Status { get; init; } = "Unknown";

    public DateTime? LastCheckedUtc { get; init; }

    public string? Message { get; init; }
}
