using ClientAgent.Shared.Models;

namespace ClientAgent.Service.Monitoring;

public interface IMonitoringModule
{
    string Name { get; }

    Task RunCycleAsync(CancellationToken cancellationToken);
}
