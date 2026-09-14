using ClientAgent.Service.Config;
using ClientAgent.Service.Options;
using ClientAgent.Service.Runtime;
using ClientAgent.Service.SystemInfo;
using ClientAgent.Shared.Models;
using Microsoft.Extensions.Options;

namespace ClientAgent.Service.Monitoring;

public sealed class ResourceMonitor : BackgroundService, IMonitoringModule
{
    private readonly ISystemInfoService _systemInfo;
    private readonly IEventPublisher _publisher;
    private readonly IAgentIdentity _identity;
    private readonly ILocalConfigCache _configCache;
    private readonly ILogger<ResourceMonitor> _logger;
    private readonly int _intervalSeconds;

    public ResourceMonitor(
        ISystemInfoService systemInfo,
        IEventPublisher publisher,
        IAgentIdentity identity,
        ILocalConfigCache configCache,
        IOptions<MonitoringOptions> options,
        ILogger<ResourceMonitor> logger)
    {
        _systemInfo = systemInfo;
        _publisher = publisher;
        _identity = identity;
        _configCache = configCache;
        _logger = logger;
        _intervalSeconds = Math.Max(5, options.Value.ResourceIntervalSeconds);
    }

    public string Name => nameof(ResourceMonitor);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Resource monitor cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
        }
    }

    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var config = await _configCache.GetConfigAsync(cancellationToken);
        var snapshot = await _systemInfo.GetSnapshotAsync(cancellationToken);

        if (snapshot.Cpu.UsagePercent >= config.CpuCriticalThreshold)
        {
            await PublishThresholdAsync("cpu", EventType.ResourceThreshold, snapshot.Cpu.UsagePercent, config.CpuCriticalThreshold, "CPU usage exceeded critical threshold", cancellationToken);
        }

        if (snapshot.Ram.UsagePercent >= config.RamCriticalThreshold)
        {
            await PublishThresholdAsync("ram", EventType.ResourceThreshold, snapshot.Ram.UsagePercent, config.RamCriticalThreshold, "RAM usage exceeded critical threshold", cancellationToken);
        }

        foreach (var partition in snapshot.Partitions.Where(p => p.UsagePercent >= config.DiskCriticalThreshold))
        {
            await PublishThresholdAsync(
                $"disk-{partition.DriveLetter.TrimEnd('\\', ':')}",
                EventType.ResourceThreshold,
                partition.UsagePercent,
                config.DiskCriticalThreshold,
                $"Disk {partition.DriveLetter} usage exceeded critical threshold",
                cancellationToken);
        }
    }

    private Task PublishThresholdAsync(
        string pointId,
        EventType type,
        double measured,
        double threshold,
        string message,
        CancellationToken cancellationToken)
    {
        return _publisher.PublishAsync(new MonitoringEvent
        {
            EventId = Guid.NewGuid(),
            AgentId = _identity.AgentId,
            MonitorPointId = pointId,
            TimestampUtc = DateTime.UtcNow,
            EventType = type,
            Severity = Severity.Critical,
            Status = EventStatus.Critical,
            MeasuredValue = measured,
            Threshold = threshold,
            Message = message,
            ConfigVersion = _configCache.GetConfigVersion(),
            AgentVersion = _identity.Version
        }, cancellationToken);
    }
}
