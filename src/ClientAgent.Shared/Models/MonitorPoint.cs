using System.Text.Json.Serialization;

namespace ClientAgent.Shared.Models;

public sealed class MonitorPoint
{
    public string MonitorPointId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public MonitorPointType Type { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GarageDeviceKind? DeviceKind { get; init; }

    public string Address { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; init; }

    public string Location { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public bool ShowInShortcut { get; init; }

    public MonitorPointAlert Alert { get; init; } = MonitorPointAlert.Problem;

    public int IntervalSeconds { get; init; } = 3;

    public double? WarningThreshold { get; init; }

    public double? CriticalThreshold { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DatabaseLogin? Database { get; init; }
}
