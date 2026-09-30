using ClientAgent.Service.Config;
using ClientAgent.Service.SystemInfo;

namespace ClientAgent.Service.Monitoring;

public sealed class ResourceMonitor : BackgroundService, IMonitoringModule
{
    private readonly ISystemInfoService _systemInfo;
    private readonly ILocalConfigCache _configCache;
    private readonly IMonitorHealthStore _health;
    private readonly ILogger<ResourceMonitor> _logger;
    private DateTime _nextCpuUtc = DateTime.MinValue;
    private DateTime _nextRamUtc = DateTime.MinValue;
    private DateTime _nextDiskUtc = DateTime.MinValue;

    public ResourceMonitor(
        ISystemInfoService systemInfo,
        ILocalConfigCache configCache,
        IMonitorHealthStore health,
        ILogger<ResourceMonitor> logger)
    {
        _systemInfo = systemInfo;
        _configCache = configCache;
        _health = health;
        _logger = logger;
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

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var config = await _configCache.GetConfigAsync(cancellationToken);
        var general = _configCache.GetGeneral();
        var now = DateTime.UtcNow;
        var checkCpu = now >= _nextCpuUtc;
        var checkRam = now >= _nextRamUtc;
        var checkDisk = now >= _nextDiskUtc;
        if (!checkCpu && !checkRam && !checkDisk)
        {
            return;
        }

        if (checkCpu)
        {
            _nextCpuUtc = now.AddSeconds(general.Seconds(general.CpuIntervalSeconds, 3));
        }

        if (checkRam)
        {
            _nextRamUtc = now.AddSeconds(general.Seconds(general.RamIntervalSeconds, 3));
        }

        if (checkDisk)
        {
            _nextDiskUtc = now.AddSeconds(general.Seconds(general.DiskIntervalSeconds, 15));
        }

        _logger.LogInformation("[Resources] Checking {Parts}...",
            string.Join(", ", new[] { checkCpu ? "CPU" : null, checkRam ? "RAM" : null, checkDisk ? "disks" : null }.OfType<string>()));
        var cpu = checkCpu ? await _systemInfo.GetCpuAsync(cancellationToken) : null;
        var ram = checkRam ? await _systemInfo.GetRamAsync(cancellationToken) : null;
        var partitions = checkDisk ? await _systemInfo.GetPartitionsAsync(cancellationToken) : null;

        if (cpu is not null && cpu.UsagePercent >= config.CpuCriticalThreshold)
        {
            _logger.LogWarning("[Resources] CPU: CRITICAL - {Usage:0.#}% used (threshold {Threshold}%)", cpu.UsagePercent, config.CpuCriticalThreshold);
            _health.SetIssue("cpu", "Critical", "CPU threshold", IssueText.CpuHigh(cpu.UsagePercent), "cpu");
        }
        else if (checkCpu)
        {
            _logger.LogInformation("[Resources] CPU: OK - {Usage:0.#}% used (threshold {Threshold}%)", cpu?.UsagePercent, config.CpuCriticalThreshold);
            _health.ClearIssue("cpu");
        }

        if (ram is not null && ram.UsagePercent >= config.RamCriticalThreshold)
        {
            _logger.LogWarning("[Resources] RAM: CRITICAL - {Usage:0.#}% used (threshold {Threshold}%)", ram.UsagePercent, config.RamCriticalThreshold);
            _health.SetIssue("ram", "Critical", "RAM threshold", IssueText.RamHigh(ram.UsagePercent), "ram");
        }
        else if (checkRam)
        {
            _logger.LogInformation("[Resources] RAM: OK - {Usage:0.#}% used (threshold {Threshold}%)", ram?.UsagePercent, config.RamCriticalThreshold);
            _health.ClearIssue("ram");
        }

        if (partitions is null)
        {
            return;
        }

        var criticalDrives = partitions
            .Where(p => p.UsagePercent >= config.DiskCriticalThreshold)
            .Select(p => p.DriveLetter.TrimEnd('\\', ':'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var partition in partitions)
        {
            var drive = partition.DriveLetter.TrimEnd('\\', ':');
            var key = $"disk-{drive}";
            if (criticalDrives.Contains(drive))
            {
                _logger.LogWarning("[Resources] Disk {Drive}: CRITICAL - {Usage:0.#}% used (threshold {Threshold}%)",
                    drive, partition.UsagePercent, config.DiskCriticalThreshold);
                _health.SetIssue(key, "Critical", "Disk threshold", IssueText.DiskHigh(partition.DriveLetter, partition.UsagePercent), key);
            }
            else
            {
                _logger.LogInformation("[Resources] Disk {Drive}: OK - {Usage:0.#}% used (threshold {Threshold}%)",
                    drive, partition.UsagePercent, config.DiskCriticalThreshold);
                _health.ClearIssue(key);
            }
        }
    }
}
