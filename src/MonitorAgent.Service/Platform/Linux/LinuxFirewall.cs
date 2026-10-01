namespace MonitorAgent.Service.Platform.Linux;

/// <summary>
/// Opens the port in ufw (Ubuntu, Debian) or firewalld (Fedora, RHEL, Rocky, openSUSE), whichever is running.
/// The opened port is remembered in a file so the rule can be removed after a restart or a port change.
/// </summary>
public sealed class LinuxFirewall : IFirewall
{
    private static readonly string PortFile = Path.Combine(AgentPaths.StateFolder, "firewall-port");

    public string Name { get; private set; } = "firewall";

    public bool Allow(int? port)
    {
        var tool = ActiveTool();
        Name = tool ?? "firewall (none running, so the port is already open)";
        var previous = int.TryParse(LinuxFiles.ReadLine(PortFile), out var saved) ? saved : (int?)null;

        if (tool is null)
        {
            Remember(port);
            return true;
        }

        if (previous is not null && previous != port)
        {
            Close(tool, previous.Value);
        }

        if (port is null)
        {
            Remember(null);
            return true;
        }

        var opened = tool == "ufw"
            ? Command.Run("ufw", $"allow {port}/tcp comment \"MonitorAgent API\"") is { Succeeded: true }
            : Command.Run("firewall-cmd", $"--permanent --add-port={port}/tcp") is { Succeeded: true }
              && Command.Run("firewall-cmd", "--reload") is { Succeeded: true };
        if (opened)
        {
            Remember(port);
        }

        return opened;
    }

    private static string? ActiveTool()
    {
        if (Command.Run("ufw", "status", 5000) is { Succeeded: true } ufw && ufw.Output.Contains("Status: active", StringComparison.OrdinalIgnoreCase))
        {
            return "ufw";
        }

        return Command.Run("firewall-cmd", "--state", 5000) is { Succeeded: true } firewalld && firewalld.Output.Trim() == "running"
            ? "firewalld"
            : null;
    }

    private static void Close(string tool, int port)
    {
        if (tool == "ufw")
        {
            Command.Run("ufw", $"delete allow {port}/tcp");
        }
        else
        {
            Command.Run("firewall-cmd", $"--permanent --remove-port={port}/tcp");
            Command.Run("firewall-cmd", "--reload");
        }
    }

    private static void Remember(int? port)
    {
        try
        {
            if (port is null)
            {
                File.Delete(PortFile);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PortFile)!);
                File.WriteAllText(PortFile, port.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
