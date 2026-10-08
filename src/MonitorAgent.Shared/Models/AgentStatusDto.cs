namespace MonitorAgent.Shared.Models;

public sealed record AgentStatusDto(
    string AgentId,
    string Status,
    string Version,
    TimeSpan Uptime,
    bool MadkhalConnected,
    bool CentralConnected,
    string ConfigVersion = "1",
    DateTime? LastSyncUtc = null,
    string[]? ListenUrls = null,
    List<string>? Addresses = null,
    string? AccessRole = null);
