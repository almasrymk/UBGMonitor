namespace ClientAgent.Shared.Models;

public sealed class DiskPartition
{
    public string DriveLetter { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string FileSystem { get; init; } = string.Empty;

    public double TotalGB { get; init; }

    public double UsedGB { get; init; }

    public double FreeGB { get; init; }

    public double UsagePercent { get; init; }
}
