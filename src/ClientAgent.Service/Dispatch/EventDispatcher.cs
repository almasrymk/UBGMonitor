using ClientAgent.Service.Options;
using ClientAgent.Service.Outbox;
using Microsoft.Extensions.Options;

namespace ClientAgent.Service.Dispatch;

public sealed class EventDispatcher
{
    private readonly IOutboxRepository _outbox;
    private readonly IRoutingService _routing;
    private readonly IEventSender _sender;
    private readonly ILogger<EventDispatcher> _logger;
    private readonly int _batchSize;

    public EventDispatcher(
        IOutboxRepository outbox,
        IRoutingService routing,
        IEventSender sender,
        IOptions<OutboxOptions> options,
        ILogger<EventDispatcher> logger)
    {
        _outbox = outbox;
        _routing = routing;
        _sender = sender;
        _logger = logger;
        _batchSize = Math.Max(1, options.Value.BatchSize);
    }

    public async Task DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var pending = await _outbox.GetPendingAsync(_batchSize, cancellationToken);
        if (pending.Count == 0)
        {
            return;
        }

        var destination = await _routing.DecideDestinationAsync(cancellationToken);
        foreach (var group in pending.GroupBy(e => e.EventType))
        {
            var batch = group.ToList();
            var sent = await _sender.SendAsync(batch, destination, cancellationToken);
            foreach (var item in batch)
            {
                if (sent)
                {
                    await _outbox.MarkAsSentAsync(item.EventId, destination.ToString(), cancellationToken);
                }
                else
                {
                    await _outbox.IncrementRetryAsync(item.EventId, cancellationToken);
                }
            }
        }

        _logger.LogDebug("Dispatched {Count} pending events to {Destination}", pending.Count, destination);
    }
}
