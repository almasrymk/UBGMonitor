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

/// <summary>Applies a cloud configuration document (05 section 8): validate, apply atomically, persist; false keeps the previous one.</summary>
public interface ICloudConfigApplier
{
    Task<(bool Success, string? Error)> ApplyAsync(int version, string json, CancellationToken cancellationToken);
}

/// <summary>Without an applier the connector checks the document's shape and accepts it (the agent keeps its local settings).</summary>
internal sealed class ShapeOnlyConfigApplier : ICloudConfigApplier
{
    public Task<(bool Success, string? Error)> ApplyAsync(int version, string json, CancellationToken cancellationToken) =>
        Task.FromResult(CloudConfigDocument.TryParse(json, out _, out var error) ? (true, (string?)null) : (false, error));
}

public sealed record CloudThreshold(double WarningPercent, double CriticalPercent, int ForSeconds, double? ClearBelowPercent);

/// <summary>The parts of the configuration document the agent uses.</summary>
public sealed record CloudConfigDocument(int Version, int SampleSeconds, CloudThreshold Cpu, CloudThreshold Ram, CloudThreshold Disk)
{
    public static bool TryParse(string json, out CloudConfigDocument? document, out string? error)
    {
        document = null;
        try
        {
            using var parsed = System.Text.Json.JsonDocument.Parse(json);
            var root = parsed.RootElement;
            var thresholds = root.GetProperty("thresholds");
            CloudThreshold Read(string name)
            {
                var t = thresholds.GetProperty(name);
                return new CloudThreshold(
                    t.GetProperty("warningPercent").GetDouble(), t.GetProperty("criticalPercent").GetDouble(), t.TryGetProperty("forSeconds", out var f) ? f.GetInt32() : 0,
                    t.TryGetProperty("clearBelowPercent", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.Number ? c.GetDouble() : null);
            }

            var cpu = Read("cpu");
            var ram = Read("ram");
            var disk = Read("disk");
            foreach (var (name, t) in new[] { ("cpu", cpu), ("ram", ram), ("disk", disk) })
            {
                if (t.WarningPercent is <= 0 or > 100 || t.CriticalPercent is <= 0 or > 100 || t.CriticalPercent <= t.WarningPercent)
                {
                    error = $"thresholds.{name}: warning and critical must be 1-100 with critical above warning.";
                    return false;
                }
            }

            var sample = root.TryGetProperty("telemetry", out var telemetry) && telemetry.TryGetProperty("sampleSeconds", out var s) ? s.GetInt32() : 5;
            document = new CloudConfigDocument(root.TryGetProperty("version", out var v) ? v.GetInt32() : 0, Math.Clamp(sample, 1, 30), cpu, ram, disk);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            error = $"The configuration document is not valid: {ex.Message}";
            return false;
        }
    }
}
