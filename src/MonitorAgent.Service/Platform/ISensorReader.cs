using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Platform;

/// <summary>Temperatures, fans, voltages, battery and disk health; whatever this system exposes.</summary>
public interface ISensorReader
{
    /// <summary>Starts the slow first read in the background so the first request does not wait for it.</summary>
    void Warmup();

    SensorsInfo Read();
}
