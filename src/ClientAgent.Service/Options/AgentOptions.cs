namespace ClientAgent.Service.Options;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public string AgentId { get; set; } = string.Empty;

    public string Version { get; set; } = "1.0.0";

    public int HeartbeatIntervalSeconds { get; set; } = 60;
}
