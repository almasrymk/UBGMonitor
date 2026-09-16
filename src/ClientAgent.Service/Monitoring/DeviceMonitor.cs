using System.Net.NetworkInformation;
using ClientAgent.Service.Config;
using ClientAgent.Service.Options;
using ClientAgent.Shared.Models;
using Microsoft.Extensions.Options;

namespace ClientAgent.Service.Monitoring;

public sealed class DeviceMonitor : BackgroundService, IMonitoringModule
{
    private readonly ILocalConfigCache _configCache;
    private readonly IMonitorHealthStore _health;
    private readonly ILogger<DeviceMonitor> _logger;
    private readonly int _intervalSeconds;
    private readonly Dictionary<string, bool> _lastUp = new(StringComparer.OrdinalIgnoreCase);

    public DeviceMonitor(
        ILocalConfigCache configCache,
        IMonitorHealthStore health,
        IOptions<MonitoringOptions> options,
        ILogger<DeviceMonitor> logger)
    {
        _configCache = configCache;
        _health = health;
        _logger = logger;
        _intervalSeconds = Math.Max(5, options.Value.DeviceIntervalSeconds);
    }

    public string Name => nameof(DeviceMonitor);

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
                _logger.LogError(ex, "Device monitor cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
        }
    }

    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var config = await _configCache.GetConfigAsync(cancellationToken);
        var devices = config.MonitorPoints.Where(p => p.Enabled && p.Type == MonitorPointType.Device);

        foreach (var device in devices)
        {
            var up = await PingAsync(device.Address, cancellationToken);
            _lastUp.TryGetValue(device.MonitorPointId, out var previousUp);

            _health.SetPointHealth(device.MonitorPointId, up, up ? "Device is reachable" : "Device is unreachable");
            if (up)
            {
                _health.ClearIssue($"device:{device.MonitorPointId}");
            }
            else
            {
                _health.SetIssue(
                    $"device:{device.MonitorPointId}",
                    "Critical",
                    "Device unreachable",
                    IssueText.DeviceDown(device.DisplayName),
                    device.MonitorPointId);
            }

            if (previousUp && !up)
            {
                _logger.LogWarning("Device {Name} ({Address}) is unreachable", device.DisplayName, device.Address);
            }
            else if (!previousUp && up && _lastUp.ContainsKey(device.MonitorPointId))
            {
                _logger.LogInformation("Device {Name} ({Address}) recovered", device.DisplayName, device.Address);
            }

            _lastUp[device.MonitorPointId] = up;
        }
    }

    private static async Task<bool> PingAsync(string address, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, 1000);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }
}
