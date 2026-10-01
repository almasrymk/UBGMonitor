namespace MonitorAgent.Shared.Models;

/// <summary>Live activity of all physical disks together, like the Task Manager disk graph.</summary>
public sealed class DiskActivityDto
{
    public double ActiveTimePercent { get; set; }

    public double ReadBytesPerSecond { get; set; }

    public double WriteBytesPerSecond { get; set; }

    public double ResponseMs { get; set; }

    public double QueueLength { get; set; }
}
