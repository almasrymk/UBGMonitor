namespace ClientAgent.Shared.Models;

public sealed class HardwareInfo
{
    public string Manufacturer { get; init; } = "Unknown";

    public string Model { get; init; } = "Unknown";

    public string SerialNumber { get; init; } = "Unknown";

    public string Cpu { get; init; } = "Unknown";

    public string CoresThreads { get; init; } = "Unknown";

    public string CpuSpeed { get; init; } = "Unknown";

    public string Ram { get; init; } = "Unknown";

    public string Gpu { get; init; } = "Unknown";

    public string Disk { get; init; } = "Unknown";

    public string Motherboard { get; init; } = "Unknown";

    public string BiosVersion { get; init; } = "Unknown";

    public string BiosDate { get; init; } = "Unknown";

    public string GpuModel { get; init; } = "Unknown";

    public double GpuVramGB { get; init; }
}
