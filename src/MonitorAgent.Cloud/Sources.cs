namespace MonitorAgent.Cloud;

/// <summary>One reading for the minute aggregates (AG-6). Percentages are 0-100; rates are bytes per second.</summary>
public sealed record CloudSample(
    DateTimeOffset At,
    double Cpu,
    double Ram,
    double DiskPercentMax,
    long UptimeSeconds,
    double? DiskActive = null,
    long? ReadBps = null,
    long? WriteBps = null,
    double? DiskResponseMs = null,
    long? RxBps = null,
    long? TxBps = null,
    double? PingMs = null,
    double? PacketLossPercent = null,
    double? TempC = null);

public sealed record CloudDisk(string Drive, string Label, string FileSystem, double TotalGb, double UsedGb, double FreeGb);

/// <summary>An active problem of the agent. <see cref="Severity"/> is Critical, Warning or Info.</summary>
public sealed record CloudIssue(string Key, string Severity, string Title, string Message, DateTimeOffset At, string? MonitorPointKey = null);

/// <summary>A monitor point and its latest status (Healthy, Warning, Critical or Unknown).</summary>
public sealed record CloudPoint(
    string Key, string DisplayName, string Type, string Target, bool Enabled, string Status, string? Message, double? ResponseMs, DateTimeOffset? LastChecked,
    DateTimeOffset? StatusSince, int IntervalSeconds);

public sealed record CloudHost(
    string Hostname, string OsFamily, string OsName, string OsVersion, string Architecture, string AgentVersion, string? LocalIp, string? MacAddress, DateTimeOffset? BootTime);

/// <summary>What the connector reads from the agent. The service implements it over its existing collectors.</summary>
public interface ICloudAgentSource
{
    /// <summary>The stable device fingerprint (AG-2), also the Licensing device id.</summary>
    string Fingerprint { get; }

    CloudHost Host();

    Task<CloudSample> SampleAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CloudDisk>> DisksAsync(CancellationToken cancellationToken);

    IReadOnlyList<CloudIssue> Issues();

    IReadOnlyList<CloudPoint> MonitorPoints();

    /// <summary>The detailed picture behind the device screen (05 section 5), serialised as JSON.</summary>
    Task<object?> SnapshotAsync(CancellationToken cancellationToken);

    /// <summary>Inventory documents by kind (hardware, os, network, disks, programs, services, users, sensors).</summary>
    Task<IReadOnlyDictionary<string, object>> InventoryAsync(CancellationToken cancellationToken);
}

/// <summary>Receives licence updates from the cloud (enrollment and <c>LicenseUpdate</c>, AG-7).</summary>
public interface ICloudLicenseSink
{
    void Update(string state, string? reasonCode, string? token, DateTimeOffset? checkAfter);
}

internal sealed class NoLicenseSink : ICloudLicenseSink
{
    public void Update(string state, string? reasonCode, string? token, DateTimeOffset? checkAfter)
    {
    }
}
