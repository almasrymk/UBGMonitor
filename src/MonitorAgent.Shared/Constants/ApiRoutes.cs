namespace MonitorAgent.Shared.Constants;

public static class ApiRoutes
{
    public const string Status = "/api/status";
    public const string Snapshot = "/api/snapshot";
    public const string Cpu = "/api/cpu";
    public const string Ram = "/api/ram";
    public const string Network = "/api/network";
    public const string DiskPartitions = "/api/disks/partitions";
    public const string DiskPhysical = "/api/disks/physical";
    public const string DiskActivity = "/api/disks/activity";
    public const string Hardware = "/api/hardware";
    public const string HardwareLevels = "/api/hardware/levels";
    public const string Os = "/api/os";
    public const string Sensors = "/api/sensors";
    public const string ProcessesTop = "/api/processes/top";
    public const string MonitorPoints = "/api/monitorpoints";
    public const string Issues = "/api/issues";
    public const string Notifications = "/api/notifications";
    public const string Internet = "/api/internet";
    public const string InternetSpeedTest = "/api/internet/speedtest";
    public const string AgentConfig = "/api/agent/config";
    public const string Settings = "/api/settings";
    public const string DatabaseTest = "/api/database/test";

    /// <summary>Header with the remote access key, needed from other computers when the service has one.</summary>
    public const string AccessKeyHeader = "X-Agent-Key";

    public const string Reports = "/api/reports";
    public const string ReportSubjects = "/api/reports/subjects";

    public const string License = "/api/license";
    public const string LicenseActivate = "/api/license/activate";
    public const string LicenseDeactivate = "/api/license/deactivate";
    public const string LicenseRefresh = "/api/license/refresh";
}
