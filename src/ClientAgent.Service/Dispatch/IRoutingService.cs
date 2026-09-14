using ClientAgent.Shared.Models;

namespace ClientAgent.Service.Dispatch;

public interface IRoutingService
{
    Task<Destination> DecideDestinationAsync(CancellationToken cancellationToken = default);
}
