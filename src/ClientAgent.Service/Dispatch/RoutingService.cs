using ClientAgent.Service.Connectivity;
using ClientAgent.Shared.Models;

namespace ClientAgent.Service.Dispatch;

public sealed class RoutingService : IRoutingService
{
    private readonly IConnectivityTracker _connectivity;

    public RoutingService(IConnectivityTracker connectivity)
    {
        _connectivity = connectivity;
    }

    public Task<Destination> DecideDestinationAsync(CancellationToken cancellationToken = default)
    {
        if (_connectivity.MadkhalAvailable)
        {
            return Task.FromResult(Destination.Madkhal);
        }

        if (_connectivity.CentralAvailable)
        {
            return Task.FromResult(Destination.Central);
        }

        return Task.FromResult(Destination.Local);
    }
}
