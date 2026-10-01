namespace MonitorAgent.Service.Platform;

/// <summary>Opens the API port to other devices while the API listens beyond this computer.</summary>
public interface IFirewall
{
    /// <summary>The firewall's name in the log ("Windows Firewall", "ufw"...).</summary>
    string Name { get; }

    /// <summary>Allows incoming TCP on the port, or removes the rule when the port is null.</summary>
    /// <returns>False when the firewall could not be changed (or there is none to change).</returns>
    bool Allow(int? port);
}

public sealed class NoFirewall : IFirewall
{
    public string Name => "firewall";

    public bool Allow(int? port) => false;
}
