using ClientAgent.Service.Config;
using ClientAgent.Service.Options;
using ClientAgent.Service.SystemInfo;
using Microsoft.Extensions.Options;

namespace ClientAgent.Service.Monitoring;

public sealed class ResourceMonitor : BackgroundService, IMonitoringModule
{
    private readonly ISystemInfoService _systemInfo;
    private readonly ILocalConfigCache _configCache;
    private readonly IMonitorHealthStore _health;
    private readonly ILogger<ResourceMonitor> _logger;
    private readonly int _intervalSeconds;

    public ResourceMonitor(
        ISystemInfoService systemInfo,
        ILocalConfigCache configCache,
        IMonitorHealthStore health,
        IOptions<MonitoringOptions> options,
        ILogger<ResourceMonitor> logger)
    {
        _systemInfo = systemInfo;
        _configCache = configCache;
        _health = health;
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
            _logger.LogWarning("CPU usage {Usage}% exceeded critical threshold {Threshold}%", snapshot.Cpu.UsagePercent, config.CpuCriticalThreshold);
            _health.SetIssue("cpu", "Critical", "CPU threshold", IssueText.CpuHigh(snapshot.Cpu.UsagePercent), "cpu");
        }
        else
        {
            _health.ClearIssue("cpu");
        }

        if (snapshot.Ram.UsagePercent >= config.RamCriticalThreshold)
        {
            _logger.LogWarning("RAM usage {Usage}% exceeded critical threshold {Threshold}%", snapshot.Ram.UsagePercent, config.RamCriticalThreshold);
            _health.SetIssue("ram", "Critical", "RAM threshold", IssueText.RamHigh(snapshot.Ram.UsagePercent), "ram");
        }
        else
        {
            _health.ClearIssue("ram");
        }

        var criticalDrives = snapshot.Partitions
            .Where(p => p.UsagePercent >= config.DiskCriticalThreshold)
            .Select(p => p.DriveLetter.TrimEnd('\\', ':'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var partition in snapshot.Partitions)
        {
            var drive = partition.DriveLetter.TrimEnd('\\', ':');
            var key = $"disk-{drive}";
            if (criticalDrives.Contains(drive))
            {
                _logger.LogWarning(
                    "Disk {Drive} usage {Usage}% exceeded critical threshold {Threshold}%",
                    partition.DriveLetter,
                    partition.UsagePercent,
                    config.DiskCriticalThreshold);
                _health.SetIssue(key, "Critical", "Disk threshold", IssueText.DiskHigh(partition.DriveLetter, partition.UsagePercent), key);
            }
            else
            {
                _health.ClearIssue(key);
            }
        }
    }
}
