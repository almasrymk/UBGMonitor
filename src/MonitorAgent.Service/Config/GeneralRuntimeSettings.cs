namespace MonitorAgent.Service.Config;

public sealed class GeneralRuntimeSettings
{
    public string MachineName { get; set; } = string.Empty;

    public int RefreshInterval { get; set; } = 3;

    public int InternetIntervalSeconds { get; set; } = 5;

    public int CpuIntervalSeconds { get; set; } = 3;

    public int RamIntervalSeconds { get; set; } = 3;

    public int NetworkIntervalSeconds { get; set; } = 3;

    public int DiskIntervalSeconds { get; set; } = 15;

    public int HardwareOsIntervalSeconds { get; set; } = 30;

    /// <summary>Seconds between the end of one automatic speed test and the start of the next; 0 disables them.</summary>
    public int SpeedTestIntervalSeconds { get; set; } = 1800;

    public bool NotificationsEnabled { get; set; } = true;

    public const int MinRetentionDays = 30;
    public const int MaxRetentionDays = 365;
    public const int DefaultRetentionDays = 90;

    /// <summary>Logs, saved data and report history older than this are deleted (30 to 365 days).</summary>
    public int DataRetentionDays { get; set; } = DefaultRetentionDays;

    /// <summary>The address the local API listens on: 127.0.0.1 (this computer only), 0.0.0.0 (every network card) or one IP of this computer.</summary>
    public string ServiceListenAddress { get; set; } = "127.0.0.1";

    /// <summary>The local API port; 0 uses LocalApi:Port.</summary>
    public int ServicePort { get; set; }

    /// <summary>When set, requests from other computers must send it in the X-Agent-Key header.</summary>
    public string RemoteAccessKey { get; set; } = string.Empty;

    public int RetentionDays => DataRetentionDays <= 0
        ? DefaultRetentionDays
        : Math.Clamp(DataRetentionDays, MinRetentionDays, MaxRetentionDays);

    public string DisplayName
        => string.IsNullOrWhiteSpace(MachineName) ? Environment.MachineName : MachineName.Trim();

    public int Seconds(int value, int fallback) => value < 1 ? fallback : value;
}
