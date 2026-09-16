namespace ClientAgent.Service.Monitoring;

public interface IMonitoringModule
{
    string Name { get; }

    Task RunCycleAsync(CancellationToken cancellationToken);
}
