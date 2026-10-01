namespace MonitorAgent.Service.Platform.Windows;

public sealed class WindowsFirewall : IFirewall
{
    public const string RuleName = "MonitorAgent API";

    public string Name => "Windows Firewall";

    public bool Allow(int? port)
    {
        if (Command.Run("netsh", $"advfirewall firewall delete rule name=\"{RuleName}\"") is null)
        {
            return false;
        }

        return port is null
               || Command.Run("netsh", $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={port}") is { Succeeded: true };
    }
}
