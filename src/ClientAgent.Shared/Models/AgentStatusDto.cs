namespace ClientAgent.Shared.Models;

public sealed record AgentStatusDto(
    string AgentId,
    string Status,
    string Version,
    TimeSpan Uptime,
    long PendingCount,
    bool MadkhalConnected,
    bool CentralConnected);
