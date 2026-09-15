namespace ClientAgent.Shared.Models;

public sealed class HardwareInfo
{
    public string Manufacturer { get; init; } = "Unknown";

    public string Model { get; init; } = "Unknown";

    public string SerialNumber { get; init; } = "Unknown";

    public string BiosVersion { get; init; } = "Unknown";

    public string Motherboard { get; init; } = "Unknown";

    public string GpuModel { get; init; } = "Unknown";

    public double GpuVramGB { get; init; }
}
