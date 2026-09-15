namespace ClientAgent.Service.Options;

public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    public int ResourceIntervalSeconds { get; set; } = 30;

    public int DeviceIntervalSeconds { get; set; } = 15;

    public int DatabaseIntervalSeconds { get; set; } = 30;

    public int MadkhalIntervalSeconds { get; set; } = 20;
}
