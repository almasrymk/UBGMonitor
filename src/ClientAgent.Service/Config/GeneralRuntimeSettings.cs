namespace ClientAgent.Service.Config;

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

    public string DisplayName
        => string.IsNullOrWhiteSpace(MachineName) ? Environment.MachineName : MachineName.Trim();

    public int Seconds(int value, int fallback) => value < 1 ? fallback : value;
}
