using MonitorAgent.Cloud;
using MonitorAgent.Service.Config;
using MonitorAgent.Service.Platform;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Cloud;

/// <summary>
/// Applies the cloud's thresholds to the agent's runtime configuration (M8). The agent's monitors alert on one critical
/// level per resource, so the document's <c>criticalPercent</c> values are used; warning levels, durations and clear
/// levels wait for the single evaluator of AG-8. Local monitor points stay as they are. The applied document is kept in
/// <c>cloud-config.json</c>; an invalid document is refused and nothing changes.
/// </summary>
public sealed class ServiceConfigApplier(ILocalConfigCache config, ILogger<ServiceConfigApplier> logger) : ICloudConfigApplier
{
    public async Task<(bool Success, string? Error)> ApplyAsync(int version, string json, CancellationToken cancellationToken)
    {
        if (!CloudConfigDocument.TryParse(json, out var document, out var error) || document is null)
            return (false, error);
        try
        {
            var current = await config.GetConfigAsync(cancellationToken);
            await config.SaveConfigAsync(new AgentRuntimeConfig
            {
                ConfigVersion = $"cloud-{version}",
                AgentId = current.AgentId,
                MonitorPoints = current.MonitorPoints,
                DatabaseConnectionString = current.DatabaseConnectionString,
                CpuCriticalThreshold = document.Cpu.CriticalPercent,
                RamCriticalThreshold = document.Ram.CriticalPercent,
                DiskCriticalThreshold = document.Disk.CriticalPercent,
            }, cancellationToken);
            Directory.CreateDirectory(AgentPaths.StateFolder);
            var path = Path.Combine(AgentPaths.StateFolder, "cloud-config.json");
            await File.WriteAllTextAsync(path + ".tmp", json, cancellationToken);
            File.Move(path + ".tmp", path, overwrite: true);
            logger.LogInformation("[Cloud] Thresholds of version {Version}: CPU {Cpu}%, RAM {Ram}%, disk {Disk}%", version, document.Cpu.CriticalPercent, document.Ram.CriticalPercent,
                document.Disk.CriticalPercent);
            return (true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, $"The configuration could not be saved: {ex.Message}");
        }
    }
}
