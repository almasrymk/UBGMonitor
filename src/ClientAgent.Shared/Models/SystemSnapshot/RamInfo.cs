namespace ClientAgent.Shared.Models;

public sealed class RamInfo
{
    public double TotalGB { get; init; }

    public double UsedGB { get; init; }

    public double FreeGB { get; init; }

    public double CachedGB { get; init; }

    public double UsagePercent { get; init; }

    public double SwapTotalGB { get; init; }

    public double SwapUsedGB { get; init; }
}
