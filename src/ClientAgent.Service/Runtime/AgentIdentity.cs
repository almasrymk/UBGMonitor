using ClientAgent.Service.Options;
using Microsoft.Extensions.Options;

namespace ClientAgent.Service.Runtime;

public interface IAgentIdentity
{
    string AgentId { get; }

    string Version { get; }

    DateTime StartedAtUtc { get; }

    TimeSpan Uptime { get; }
}

public sealed class AgentIdentity : IAgentIdentity
{
    public AgentIdentity(IOptions<AgentOptions> options)
    {
        var value = options.Value;
        AgentId = string.IsNullOrWhiteSpace(value.AgentId)
            ? $"{Environment.MachineName}-{Guid.NewGuid():N}"
            : value.AgentId;
        Version = value.Version;
        StartedAtUtc = DateTime.UtcNow;
    }

    public string AgentId { get; }

    public string Version { get; }

    public DateTime StartedAtUtc { get; }

    public TimeSpan Uptime => DateTime.UtcNow - StartedAtUtc;
}
