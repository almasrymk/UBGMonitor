using System.Net.NetworkInformation;
using ClientAgent.Service.Config;
using ClientAgent.Service.Options;
using ClientAgent.Service.Runtime;
using ClientAgent.Shared.Models;
using Microsoft.Extensions.Options;

namespace ClientAgent.Service.Monitoring;

public sealed class DeviceMonitor : BackgroundService, IMonitoringModule
{
    private readonly IEventPublisher _publisher;
    private readonly IAgentIdentity _identity;
    private readonly ILocalConfigCache _configCache;
    private readonly ILogger<DeviceMonitor> _logger;
    private readonly int _intervalSeconds;
    private readonly Dictionary<string, EventStatus> _lastStatus = new(StringComparer.OrdinalIgnoreCase);

    public DeviceMonitor(
        IEventPublisher publisher,
        IAgentIdentity identity,
        ILocalConfigCache configCache,
        IOptions<MonitoringOptions> options,
        ILogger<DeviceMonitor> logger)
    {
        _publisher = publisher;
        _identity = identity;
        _configCache = configCache;
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
            var status = up ? EventStatus.Up : EventStatus.Down;
            _lastStatus.TryGetValue(device.MonitorPointId, out var previous);

            if (previous == EventStatus.Down && status == EventStatus.Up)
            {
                await PublishAsync(device, EventType.Recovery, Severity.Info, EventStatus.Up, "Device recovered", cancellationToken);
            }
            else
            {
                await PublishAsync(
                    device,
                    EventType.Connectivity,
                    status == EventStatus.Up ? Severity.Info : Severity.Critical,
                    status,
                    status == EventStatus.Up ? "Device is reachable" : "Device is unreachable",
                    cancellationToken);
            }

            _lastStatus[device.MonitorPointId] = status;
        }
    }

    private async Task PublishAsync(
        MonitorPoint device,
        EventType type,
        Severity severity,
        EventStatus status,
        string message,
        CancellationToken cancellationToken)
    {
        await _publisher.PublishAsync(new MonitoringEvent
        {
            EventId = Guid.NewGuid(),
            AgentId = _identity.AgentId,
            MonitorPointId = device.MonitorPointId,
            TimestampUtc = DateTime.UtcNow,
            EventType = type,
            Severity = severity,
            Status = status,
            Message = $"{device.DisplayName}: {message}",
            Metadata = new Dictionary<string, string>
            {
                ["address"] = device.Address,
                ["model"] = device.Model,
                ["location"] = device.Location
            },
            ConfigVersion = _configCache.GetConfigVersion(),
            AgentVersion = _identity.Version
        }, cancellationToken);
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
