using System.Net.NetworkInformation;
using MonitorAgent.Service.Config;
using MonitorAgent.Service.Options;
using MonitorAgent.Shared.Models;
using Microsoft.Extensions.Options;

namespace MonitorAgent.Service.Monitoring;

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
        var devices = config.MonitorPoints.Where(p => p.Enabled && p.Type == MonitorPointType.Device).ToList();
        var active = new HashSet<string>(devices.Select(d => d.MonitorPointId), StringComparer.OrdinalIgnoreCase);
        foreach (var id in _lastUp.Keys.Where(id => !active.Contains(id)).ToList())
        {
            _health.ClearIssue($"device:{id}");
            _lastUp.Remove(id);
        }

        foreach (var device in devices)
        {
            _logger.LogInformation("[Device] Pinging {Name} ({Address})...", device.DisplayName, device.Address);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var (up, roundTripMs) = await PingAsync(device.Address, cancellationToken);
            if (up)
            {
                _logger.LogInformation("[Device] {Name}: OK - replied ({Ms} ms)", device.DisplayName, watch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogWarning("[Device] {Name}: CRITICAL - no reply ({Ms} ms)", device.DisplayName, watch.ElapsedMilliseconds);
            }

            _health.SetPointHealth(device.MonitorPointId, up, up ? "Device is reachable" : "Device is unreachable", responseMs: roundTripMs);
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

            _lastUp[device.MonitorPointId] = up;
        }
    }

    private static async Task<(bool Up, double? RoundTripMs)> PingAsync(string address, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return (false, null);
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, 1000);
            return reply.Status == IPStatus.Success ? (true, reply.RoundtripTime) : (false, null);
        }
        catch
        {
            return (false, null);
        }
    }
}
