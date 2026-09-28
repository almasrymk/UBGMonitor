using System.Text.Json.Serialization;
using ClientAgent.Shared.Models;
using ClientAgent.UI.Enums;

namespace ClientAgent.UI.Models;

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

public sealed class DeviceSpecSettings
{
    public int CpuMinCores { get; set; } = 4;

    public int CpuWarningPercent { get; set; } = 85;

    public int CpuProblemPercent { get; set; } = 95;

    public double RamMinGb { get; set; } = 8;

    public int RamWarningPercent { get; set; } = 85;

    public int RamProblemPercent { get; set; } = 95;

    public string DiskUnit { get; set; } = "Percent";

    public double DiskMinimum { get; set; } = 128;

    public double DiskTotalWarning { get; set; } = 90;

    public double DiskPartitionWarning { get; set; } = 90;

    public string DiskPartitionUnit { get; set; } = "Percent";

    public int DiskWarningPercent { get; set; }

    public double DiskRemainingWarning { get; set; } = 15;

    public string DiskRemainingWarningUnit { get; set; } = "Percent";

    public double DiskRemainingProblem { get; set; } = 5;

    public string DiskRemainingProblemUnit { get; set; } = "Percent";

    public double DownloadMinKbps { get; set; } = 10000;

    public double UploadMinKbps { get; set; } = 2000;

    /// <summary>Minimum internet download speed in Kbps; null in settings saved before Kbps was introduced.</summary>
    public double? InternetMinKbps { get; set; }

    /// <summary>Legacy Mbps value, kept in sync so older builds read the same minimum.</summary>
    public double InternetMinMbps { get; set; } = 10;

    [JsonIgnore]
    public double EffectiveInternetMinKbps => InternetMinKbps ?? InternetMinMbps * 1000d;

    public string OperatingSystem { get; set; } = "Windows 10";

    public MonitorPointAlert CpuAlert { get; set; } = MonitorPointAlert.Problem;

    public MonitorPointAlert RamAlert { get; set; } = MonitorPointAlert.Problem;

    public bool InternetNotify { get; set; } = true;

    public bool DownloadNotify { get; set; } = true;

    public bool UploadNotify { get; set; } = true;

    public MonitorPointAlert DiskAlert { get; set; } = MonitorPointAlert.Problem;

    public bool OsNotify { get; set; } = true;
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
