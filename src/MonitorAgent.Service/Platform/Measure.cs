using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Platform;

/// <summary>Unit conversions shared by the readers of every system.</summary>
public static class Measure
{
    public static double BytesToGb(double bytes) => bytes / 1024d / 1024d / 1024d;

    public static double Round(double value) => Math.Round(value, 2);

    public static RamInfo Ram(long totalBytes, long freeBytes, long cachedBytes, long swapTotalBytes, long swapFreeBytes)
    {
        var totalGb = BytesToGb(totalBytes);
        var freeGb = BytesToGb(freeBytes);
        var usedGb = Math.Max(0, totalGb - freeGb);
        var swapTotal = BytesToGb(swapTotalBytes);
        return new RamInfo
        {
            TotalGB = Round(totalGb),
            UsedGB = Round(usedGb),
            FreeGB = Round(freeGb),
            CachedGB = Round(BytesToGb(cachedBytes)),
            UsagePercent = Math.Round(totalGb <= 0 ? 0 : usedGb / totalGb * 100, 1),
            SwapTotalGB = Round(swapTotal),
            SwapUsedGB = Round(Math.Max(0, swapTotal - BytesToGb(swapFreeBytes)))
        };
    }
}
