namespace ClientAgent.Shared.Models;

public sealed record AgentStatusDto(
    string AgentId,
    string Status,
    string Version,
    TimeSpan Uptime,
    bool MadkhalConnected,
    bool CentralConnected,
    string ConfigVersion = "1",
    DateTime? LastSyncUtc = null);
