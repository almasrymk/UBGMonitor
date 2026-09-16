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
    public const string HardwareLevels = "/api/hardware/levels";
    public const string Os = "/api/os";
    public const string Sensors = "/api/sensors";
    public const string ProcessesTop = "/api/processes/top";
    public const string MonitorPoints = "/api/monitorpoints";
    public const string Issues = "/api/issues";
    public const string AgentConfig = "/api/agent/config";
}
