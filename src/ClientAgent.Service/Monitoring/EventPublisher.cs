using ClientAgent.Shared.Models;
using ClientAgent.Service.Messaging;
using ClientAgent.Service.Outbox;

namespace ClientAgent.Service.Monitoring;

public interface IEventPublisher
{
    Task PublishAsync(MonitoringEvent monitoringEvent, CancellationToken cancellationToken = default);
}

public sealed class EventPublisher : IEventPublisher
{
    private readonly IOutboxRepository _outbox;
    private readonly IMonitoringEventBus _eventBus;
    private readonly ILogger<EventPublisher> _logger;

    public EventPublisher(IOutboxRepository outbox, IMonitoringEventBus eventBus, ILogger<EventPublisher> logger)
    {
        _outbox = outbox;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task PublishAsync(MonitoringEvent monitoringEvent, CancellationToken cancellationToken = default)
    {
        await _outbox.WriteAsync(monitoringEvent, cancellationToken);
        try
        {
            await _eventBus.PublishAsync(monitoringEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Event {EventId} was written to outbox but not published to in-memory bus", monitoringEvent.EventId);
        }
    }
}
