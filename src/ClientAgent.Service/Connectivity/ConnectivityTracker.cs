namespace ClientAgent.Service.Connectivity;

public interface IConnectivityTracker
{
    bool MadkhalAvailable { get; }

    bool CentralAvailable { get; }

    void SetMadkhal(bool available);

    void SetCentral(bool available);
}

public sealed class ConnectivityTracker : IConnectivityTracker
{
    private int _madkhal;
    private int _central;

    public bool MadkhalAvailable => Volatile.Read(ref _madkhal) == 1;

    public bool CentralAvailable => Volatile.Read(ref _central) == 1;

    public void SetMadkhal(bool available) => Interlocked.Exchange(ref _madkhal, available ? 1 : 0);

    public void SetCentral(bool available) => Interlocked.Exchange(ref _central, available ? 1 : 0);
}
