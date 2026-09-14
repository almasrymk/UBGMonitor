namespace ClientAgent.Service.Options;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public string AgentId { get; set; } = string.Empty;

    public string Version { get; set; } = "1.0.0";

    public int HeartbeatIntervalSeconds { get; set; } = 60;
}

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public string DatabasePath { get; set; } = @"C:\ProgramData\ClientAgent\outbox.db";

    public int MaxSizeMB { get; set; } = 500;

    public int RetentionDays { get; set; } = 7;

    public int BatchSize { get; set; } = 50;
}

public sealed class RoutingOptions
{
    public const string SectionName = "Routing";

    public string MadkhalServerUrl { get; set; } = "http://madkhal.local:8080";

    public string CentralApiUrl { get; set; } = "https://api.central.local";

    public int MadkhalTimeoutSeconds { get; set; } = 5;

    public int CentralTimeoutSeconds { get; set; } = 15;
}

public sealed class LocalApiOptions
{
    public const string SectionName = "LocalApi";

    public int Port { get; set; } = 5050;
}

public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    public int ResourceIntervalSeconds { get; set; } = 30;

    public int DeviceIntervalSeconds { get; set; } = 15;

    public int DatabaseIntervalSeconds { get; set; } = 30;

    public int MadkhalIntervalSeconds { get; set; } = 20;
}
