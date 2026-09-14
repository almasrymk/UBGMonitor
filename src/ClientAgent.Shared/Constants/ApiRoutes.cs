namespace ClientAgent.Shared.Constants;

public static class ApiRoutes
{
    public const string Status = "/api/status";
    public const string Snapshot = "/api/snapshot";
    public const string Cpu = "/api/cpu";
    public const string Ram = "/api/ram";
    public const string Network = "/api/network";
    public const string DiskPartitions = "/api/disks/partitions";
    public const string DiskPhysical = "/api/disks/physical";
    public const string Hardware = "/api/hardware";
    public const string Os = "/api/os";
    public const string ProcessesTop = "/api/processes/top";
    public const string MonitorPoints = "/api/monitorpoints";
    public const string EventsRecent = "/api/events/recent";
    public const string EventsPending = "/api/events/pending";
    public const string AgentConfig = "/api/agent/config";
}
