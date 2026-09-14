using ClientAgent.Service.Config;
using ClientAgent.Service.Options;
using ClientAgent.Service.Outbox;
using ClientAgent.Service.Runtime;
using ClientAgent.Service.SystemInfo;
using ClientAgent.Shared.Models;
using Microsoft.Extensions.Options;

namespace ClientAgent.Service.Monitoring;

public sealed class HeartbeatMonitor : BackgroundService, IMonitoringModule
{
    private readonly IEventPublisher _publisher;
    private readonly IOutboxRepository _outbox;
    private readonly IAgentIdentity _identity;
    private readonly ISystemInfoService _systemInfo;
    private readonly ILocalConfigCache _configCache;
    private readonly ILogger<HeartbeatMonitor> _logger;
    private readonly int _intervalSeconds;

    public HeartbeatMonitor(
        IEventPublisher publisher,
        IOutboxRepository outbox,
        IAgentIdentity identity,
        ISystemInfoService systemInfo,
        ILocalConfigCache configCache,
        IOptions<AgentOptions> options,
        ILogger<HeartbeatMonitor> logger)
    {
        _publisher = publisher;
        _outbox = outbox;
        _identity = identity;
        _systemInfo = systemInfo;
        _configCache = configCache;
        _logger = logger;
        _intervalSeconds = Math.Max(15, options.Value.HeartbeatIntervalSeconds);
    }

    public string Name => nameof(HeartbeatMonitor);

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
                _logger.LogError(ex, "Heartbeat monitor cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
        }
    }

    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var pending = await _outbox.GetPendingCountAsync(cancellationToken);
        var os = await _systemInfo.GetOsAsync(cancellationToken);
        await _publisher.PublishAsync(new MonitoringEvent
        {
            EventId = Guid.NewGuid(),
            AgentId = _identity.AgentId,
            MonitorPointId = "agent-heartbeat",
            TimestampUtc = DateTime.UtcNow,
            EventType = EventType.Heartbeat,
            Severity = Severity.Info,
            Status = EventStatus.Ok,
            Message = "Agent heartbeat",
            ConfigVersion = _configCache.GetConfigVersion(),
            AgentVersion = _identity.Version,
            Metadata = new Dictionary<string, string>
            {
                ["agentId"] = _identity.AgentId,
                ["version"] = _identity.Version,
                ["uptime"] = _identity.Uptime.ToString(),
                ["pendingCount"] = pending.ToString(),
                ["osUptime"] = os.Uptime.ToString()
            }
        }, cancellationToken);
    }
}
