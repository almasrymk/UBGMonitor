using System.Threading.Channels;
using ClientAgent.Shared.Models;

namespace ClientAgent.Service.Messaging;

public interface IMonitoringEventBus
{
    ValueTask PublishAsync(MonitoringEvent monitoringEvent, CancellationToken cancellationToken = default);

    IAsyncEnumerable<MonitoringEvent> ReadAllAsync(CancellationToken cancellationToken);
}

public sealed class MonitoringEventBus : IMonitoringEventBus
{
    private readonly Channel<MonitoringEvent> _channel = Channel.CreateBounded<MonitoringEvent>(
        new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        });

    public ValueTask PublishAsync(MonitoringEvent monitoringEvent, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(monitoringEvent, cancellationToken);

    public IAsyncEnumerable<MonitoringEvent> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
