namespace ClientAgent.Service.Options;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public string DatabasePath { get; set; } = @"C:\ProgramData\ClientAgent\outbox.db";

    public int MaxSizeMB { get; set; } = 500;

    public int RetentionDays { get; set; } = 7;

    public int BatchSize { get; set; } = 50;
}
