using System.Text.Json.Serialization;
using MonitorAgent.Shared.Models;
using MonitorAgent.UI.Enums;

namespace MonitorAgent.UI.Models;

public sealed class UiAppSettings
{
    public GeneralSettings General { get; set; } = new();

    public List<MonitorPoint> MonitorPoints { get; set; } = [];

    public List<ConditionRecord> Conditions { get; set; } = [];

    public DeviceSpecSettings DeviceSpec { get; set; } = new();
}

public sealed class GeneralSettings
{
    public string MachineName { get; set; } = string.Empty;

    public int RefreshInterval { get; set; } = 3;

    public int InternetIntervalSeconds { get; set; } = 5;

    /// <summary>Seconds between the end of one automatic internet speed test and the start of the next; 0 disables them.</summary>
    public int SpeedTestIntervalSeconds { get; set; } = 1800;

    public int CpuIntervalSeconds { get; set; } = 3;

    public int RamIntervalSeconds { get; set; } = 3;

    public int NetworkIntervalSeconds { get; set; } = 3;

    public int DiskIntervalSeconds { get; set; } = 15;

    public int HardwareOsIntervalSeconds { get; set; } = 30;

    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:5050";

    public string Theme { get; set; } = "Dark";

    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>The service deletes logs, saved data and report history older than this (30 to 365 days).</summary>
    public int DataRetentionDays { get; set; } = 90;

    /// <summary>Where the service listens: 127.0.0.1 (this computer only), 0.0.0.0 (all network cards) or one IP.</summary>
    public string ServiceListenAddress { get; set; } = "127.0.0.1";

    public int ServicePort { get; set; } = 5050;

    /// <summary>Other computers must send this key to use the service; empty lets any computer in.</summary>
    public string RemoteAccessKey { get; set; } = string.Empty;
}

public sealed class ConnectionPointRecord
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public ConnectionPointKind Kind { get; set; } = ConnectionPointKind.Website;

    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 30;

    public string Url { get; set; } = "https://";

    public string ProcessName { get; set; } = string.Empty;

    public string ExecutablePath { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 1433;

    public string Database { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public DeviceKind DeviceKind { get; set; } = DeviceKind.Camera;

    public string CustomDeviceType { get; set; } = string.Empty;
}

public sealed class ConditionRecord
{
    public string Name { get; set; } = "New condition";

    public string TargetId { get; set; } = "all";

    public string Rule { get; set; } = "Unreachable";

    public string Threshold { get; set; } = "5";

    public string Severity { get; set; } = "Critical";

    public bool Enabled { get; set; } = true;
}
