using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Platform;

/// <summary>All disks together: busy time, read / write speed, response time and queue, read once per second.</summary>
public interface IDiskActivityReader
{
    /// <summary>Prepares the reading; false when this system does not expose it.</summary>
    bool Start();

    /// <summary>The activity since the previous call.</summary>
    DiskActivityDto Read();
}
