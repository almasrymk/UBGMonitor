namespace MonitorAgent.Service.Platform.Mac;

/// <summary>
/// The macOS application firewall allows or blocks programs, not ports. When it is on, the agent is added to
/// its allowed list; when it is off (the default), nothing blocks the port.
/// </summary>
public sealed class MacFirewall : IFirewall
{
    private const string Tool = "/usr/libexec/ApplicationFirewall/socketfilterfw";

    public string Name { get; private set; } = "macOS firewall";

    public bool Allow(int? port)
    {
        var state = Command.Run(Tool, "--getglobalstate", 5000);
        if (state is null || !state.Output.Contains("enabled", StringComparison.OrdinalIgnoreCase))
        {
            Name = "macOS firewall (off, so the port is already open)";
            return true;
        }

        Name = "macOS firewall";
        if (port is null || Environment.ProcessPath is not { } program)
        {
            return true;
        }

        Command.Run(Tool, $"--add \"{program}\"");
        return Command.Run(Tool, $"--unblockapp \"{program}\"") is { Succeeded: true };
    }
}
