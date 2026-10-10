namespace MonitorAgent.Shared.LogMonitoring;

public enum LogSeverity { Trace, Debug, Information, Warning, Error, Critical }
public enum LogSourceType { File, Directory }
public enum LogStartPosition { FromEnd, FromBeginning, LastMegabytes, Resume }
public enum LogFormat { Auto, PlainText, JsonLines, StructuredText, IisW3C }
public enum LogHealth { Disabled, Initializing, Scanning, Healthy, Degraded, Unavailable, PermissionDenied, PathMissing, ParserError, Backpressure, WatcherFailure, CheckpointFailure }

public sealed record LogMonitoringSettings
{
    public bool Enabled { get; init; }
    public string[] AllowedRoots { get; init; } = [];
    public int DebounceMilliseconds { get; init; } = 500;
    public int ReconcileSeconds { get; init; } = 60;
    public int MaxSources { get; init; } = 100;
    public int MaxFilesPerSource { get; init; } = 100;
    public int MaxDirectoryDepth { get; init; } = 8;
    public int MaxParallelReaders { get; init; } = 2;
    public int QueueCapacity { get; init; } = 1000;
    public int ReadBufferBytes { get; init; } = 65536;
    public int MaxEventBytes { get; init; } = 262144;
    public int MaxMultilineLines { get; init; } = 500;
    public int AssemblySeconds { get; init; } = 5;
    public int BatchEvents { get; init; } = 100;
    public int RetentionDays { get; init; } = 7;
    public int MaxEvents { get; init; } = 100000;
    public int MaxStorageMegabytes { get; init; } = 100;
    public int CleanupBatch { get; init; } = 500;
}

public sealed record LogSource
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public string Application { get; init; } = "";
    public string Description { get; init; } = "";
    public LogSourceType Type { get; init; }
    public string Path { get; init; } = "";
    public string Pattern { get; init; } = "*.log";
    public bool IncludeSubdirectories { get; init; }
    public bool Enabled { get; init; } = true;
    public LogFormat Format { get; init; }
    public string Encoding { get; init; } = "utf-8";
    public LogStartPosition StartPosition { get; init; } = LogStartPosition.FromEnd;
    public int MaximumInitialMegabytes { get; init; } = 4;
    public int ReconcileSeconds { get; init; } = 60;
    public string TimestampTimeZone { get; init; } = "UTC";
    public string? ParserPattern { get; init; }
    public Dictionary<string, LogSeverity> SeverityMapping { get; init; } = [];
    public string[] Tags { get; init; } = [];
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record LogEvent
{
    public string Id { get; init; } = "";
    public string SourceId { get; init; } = "";
    public string Application { get; init; } = "";
    public string FileIdentity { get; init; } = "";
    public string FilePath { get; init; } = "";
    public long StartOffset { get; init; }
    public long EndOffset { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string? OriginalTimestamp { get; init; }
    public DateTimeOffset IngestedUtc { get; init; } = DateTimeOffset.UtcNow;
    public LogSeverity ParsedSeverity { get; init; }
    public LogSeverity Severity { get; init; }
    public string Message { get; init; } = "";
    public string? ExceptionType { get; init; }
    public string? StackTrace { get; init; }
    public string? ErrorCode { get; init; }
    public string? CorrelationId { get; init; }
    public string? RequestId { get; init; }
    public Dictionary<string, string> Properties { get; init; } = [];
    public string Parser { get; init; } = "PlainText";
    public string ClassificationReason { get; init; } = "No explicit level";
    public string Fingerprint { get; init; } = "";
    public bool Truncated { get; init; }
    public string? IncidentId { get; init; }
}

public sealed record LogRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public string SourceId { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public LogSeverity MinimumSeverity { get; init; } = LogSeverity.Error;
    public LogSeverity Severity { get; init; } = LogSeverity.Error;
    public string? Keyword { get; init; }
    public string? ExceptionType { get; init; }
    public string? ErrorCode { get; init; }
    public string? RegexPattern { get; init; }
    public string? RecoveryPattern { get; init; }
    public int WindowSeconds { get; init; } = 60;
    public int Threshold { get; init; } = 1;
    public double? RatePerSecond { get; init; }
    public int CooldownSeconds { get; init; } = 60;
    public int MissingSeconds { get; init; }
    public int Priority { get; init; }
    public string? NotificationPolicyReference { get; init; }
}

public sealed record LogCheckpoint(string SourceId, string Identity, string Path, long Offset, long Length, string Generation);
public sealed record LogSourceStatus(string SourceId, LogHealth Health, string Message, DateTimeOffset? LastRead, DateTimeOffset? LastEvent, string[] Files);
public sealed record LogQuery(string? SourceId = null, string? Application = null, LogSeverity? Severity = null, DateTimeOffset? From = null, DateTimeOffset? To = null, string? Search = null, string? ErrorCode = null, string? CorrelationId = null, int Page = 0, int PageSize = 100);
public sealed record LogPage(LogEvent[] Items, bool HasMore);
public sealed record LogMetrics(long BytesRead, long EventsProcessed, long ParseFailures, long Overloads, long CheckpointFailures, int PendingSignals, int Watchers, int OpenHandles, LogSourceStatus[] Sources);
public sealed record LogValidation(bool Valid, string Message);
