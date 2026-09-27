using ClientAgent.Shared.Models;
using ClientAgent.UI.Enums;

namespace ClientAgent.UI.Models;

public sealed class UiAppSettings
{
    public GeneralSettings General { get; set; } = new();

    public List<MonitorPoint> MonitorPoints { get; set; } = [];

    public List<ConditionRecord> Conditions { get; set; } = [];
}

public sealed class GeneralSettings
{
    public int RefreshInterval { get; set; } = 3;

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

public sealed class ConditionRecord
{
    public string Name { get; set; } = "New condition";

    public string TargetId { get; set; } = "all";

    public string Rule { get; set; } = "Unreachable";

    public string Threshold { get; set; } = "5";

    public string Severity { get; set; } = "Critical";

    public bool Enabled { get; set; } = true;
}
