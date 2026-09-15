namespace ClientAgent.Shared.Models;

public sealed class PhysicalDisk
{
    public int Index { get; init; }

    public string Model { get; init; } = "Unknown";

    public string Type { get; init; } = "Unknown";

    public string Interface { get; init; } = "Unknown";

    public double SizeGB { get; init; }

    public double? HealthPercent { get; init; }

    public double? TemperatureC { get; init; }

    public string SmartStatus { get; init; } = "Unknown";

    public List<string> Partitions { get; init; } = [];
}
