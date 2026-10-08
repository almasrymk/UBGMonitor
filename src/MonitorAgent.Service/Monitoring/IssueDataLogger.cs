using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using MonitorAgent.Service.Config;
using MonitorAgent.Service.Reports;
using MonitorAgent.Service.Runtime;
using MonitorAgent.Service.SystemInfo;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Monitoring;

public interface IIssueDataLogger
{
    /// <summary>Queues one entry: a check that ended with a problem or warning.</summary>
    /// <param name="source">What was checked, e.g. "Device checks" or "Website check".</param>
    void Record(string source, IReadOnlyList<AgentIssueDto> issues);

    /// <summary>Queues the issues a check no longer finds; only those saved before as a problem or warning are written.</summary>
    void RecordResolved(string source, IReadOnlyList<AgentIssueDto> issues);
}

/// <summary>
/// Writes every check that ends with a problem or warning (and the moment one of them is resolved, and settings changes),
/// together with all the data the agent has at that moment,
/// to Data\yyyy-MM-dd\yyyy-MM-dd_HH\yyyy-MM-dd_HH_N.json next to the service
/// (a folder per day and per hour, and one JSON array per 5 minutes: N is 1 for :00-:04 up to 12 for :55-:59).
/// The first entry of a file ("entryType": "full") holds everything; each later one ("entryType": "changes") only holds
/// the values that differ from the entry before it, keyed by path, plus the paths that disappeared under "removed".
/// </summary>
public sealed class IssueDataLogger : BackgroundService, IIssueDataLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IServiceProvider _services;
    private readonly ILogger<IssueDataLogger> _logger;
    private readonly Channel<PendingEntry> _queue = Channel.CreateUnbounded<PendingEntry>(new UnboundedChannelOptions { SingleReader = true });
    private string? _previousFile;
    private Dictionary<string, string>? _previousLeaves;
    private readonly Dictionary<string, AgentIssueDto> _open = new(StringComparer.OrdinalIgnoreCase);

    public IssueDataLogger(IServiceProvider services, ILogger<IssueDataLogger> logger)
    {
        _services = services;
        _logger = logger;
    }

    public static string DataFolder => Platform.AgentPaths.DataFolder;

    public void Record(string source, IReadOnlyList<AgentIssueDto> issues)
    {
        if (issues.Count > 0)
        {
            _queue.Writer.TryWrite(new PendingEntry(DateTime.Now, source, issues.ToList()));
        }
    }

    public void RecordResolved(string source, IReadOnlyList<AgentIssueDto> issues)
    {
        if (issues.Count > 0)
        {
            _queue.Writer.TryWrite(new PendingEntry(DateTime.Now, source, issues.ToList(), Resolved: true));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<PendingEntry>();
        while (await _queue.Reader.WaitToReadAsync(stoppingToken))
        {
            batch.Clear();
            while (_queue.Reader.TryRead(out var entry))
            {
                batch.Add(entry);
            }

            try
            {
                batch = await PrepareAsync(batch, stoppingToken);
                if (batch.Count == 0)
                {
                    continue;
                }

                // Entries that waited together share one reading of the device data.
                _logger.LogInformation("[Data] Collecting all device data for {Count} entry(ies)...", batch.Count);
                var data = await CollectDataAsync(stoppingToken);
                foreach (var entry in batch)
                {
                    Write(entry, data);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Saving issue data failed");
            }
        }
    }

    /// <summary>
    /// Drops problems that only happened because the network / internet is down, keeps track of the problems that were
    /// saved, and turns a check that no longer finds one of them into a "Resolved" entry.
    /// </summary>
    private async Task<List<PendingEntry>> PrepareAsync(List<PendingEntry> batch, CancellationToken cancellationToken)
    {
        var filter = _services.GetRequiredService<IConnectivityFilter>();
        var reports = _services.GetRequiredService<ReportStore>();
        var kept = new List<PendingEntry>();
        foreach (var entry in batch)
        {
            if (entry.Resolved)
            {
                var fixedIssues = entry.Issues
                    .Where(issue => _open.Remove(issue.Id))
                    .Select(issue => new AgentIssueDto
                    {
                        Id = issue.Id,
                        Severity = "Resolved",
                        Title = $"Resolved: {issue.Title}",
                        Message = $"The check no longer finds this {issue.Severity.ToLowerInvariant()}. It was: {issue.Message}",
                        TimestampUtc = entry.At.ToUniversalTime(),
                        MonitorPointId = issue.MonitorPointId
                    })
                    .ToList();
                if (fixedIssues.Count > 0)
                {
                    kept.Add(entry with { Issues = fixedIssues });
                }

                foreach (var issue in fixedIssues)
                {
                    reports.CloseIncident(issue.Id, entry.At.ToUniversalTime());
                }

                continue;
            }

            var issues = await filter.RemoveNetworkDependentAsync(entry.Issues, cancellationToken);
            if (issues.Count == 0)
            {
                _logger.LogInformation("[Data] Not saved: {Source} - it needs the network, and only the network / internet problem is saved", entry.Source);
                continue;
            }

            foreach (var issue in issues.Where(issue => issue.Severity is "Critical" or "Warning"))
            {
                _open[issue.Id] = issue;
                reports.OpenIncident(issue, entry.Source, entry.At.ToUniversalTime());
            }

            kept.Add(entry with { Issues = issues.ToList() });
        }

        return kept;
    }

    private async Task<Dictionary<string, object?>> CollectDataAsync(CancellationToken cancellationToken)
    {
        var system = _services.GetRequiredService<ISystemInfoService>();
        var hardware = _services.GetRequiredService<IHardwareService>();
        var sensors = _services.GetRequiredService<ISensorsService>();
        var network = _services.GetRequiredService<INetworkService>();
        var identity = _services.GetRequiredService<IAgentIdentity>();
        var cache = _services.GetRequiredService<ILocalConfigCache>();

        var snapshot = Safe(() => system.GetSnapshotAsync(cancellationToken));
        var sensorValues = Safe(() => sensors.GetSensorsAsync(cancellationToken));
        var staticLevels = Safe(() => hardware.GetStaticLevelsAsync(cancellationToken));
        var sensorLevel = Safe(() => sensors.GetLevelAsync(cancellationToken));
        var networkLevel = Safe(() => network.GetLevelAsync(cancellationToken));
        var points = Safe(() => MonitorPointStatusBuilder.BuildAsync(_services, cancellationToken));
        await Task.WhenAll(snapshot, sensorValues, staticLevels, sensorLevel, networkLevel, points);

        return new Dictionary<string, object?>
        {
            ["machineName"] = cache.GetGeneral().DisplayName,
            ["userName"] = Environment.UserName,
            ["agentVersion"] = identity.Version,
            ["serviceUptime"] = identity.Uptime.ToString(@"d\.hh\:mm\:ss"),
            ["activeNotifications"] = _services.GetRequiredService<INotificationStore>().GetNotifications(),
            ["serviceIssues"] = _services.GetRequiredService<IMonitorHealthStore>().GetIssues(),
            ["internet"] = _services.GetRequiredService<IInternetStatus>().GetState(),
            ["monitorPoints"] = points.Result,
            ["system"] = snapshot.Result,
            ["sensors"] = sensorValues.Result,
            ["hardwareLevels"] = staticLevels.Result,
            ["sensorsLevel"] = sensorLevel.Result,
            ["networkLevel"] = networkLevel.Result
        };
    }

    private void Write(PendingEntry entry, Dictionary<string, object?> data)
    {
        var body = new Dictionary<string, object?> { ["issues"] = entry.Issues };
        foreach (var (key, value) in data)
        {
            body[key] = value;
        }

        var bodyNode = JsonSerializer.SerializeToNode(body, JsonOptions)!.AsObject();
        var leaves = new Dictionary<string, string>();
        Flatten(bodyNode, string.Empty, leaves);

        var record = new JsonObject
        {
            ["loggedAt"] = JsonSerializer.SerializeToNode(entry.At, JsonOptions),
            ["loggedAtUtc"] = JsonSerializer.SerializeToNode(entry.At.ToUniversalTime(), JsonOptions),
            ["source"] = entry.Source
        };

        try
        {
            var hourFolder = Path.Combine(DataFolder, entry.At.ToString("yyyy-MM-dd"), entry.At.ToString("yyyy-MM-dd_HH"));
            Directory.CreateDirectory(hourFolder);
            var part = entry.At.Minute / 5 + 1;
            var file = Path.Combine(hourFolder, $"{entry.At:yyyy-MM-dd_HH}_{part}.json");

            // Every file starts with a full entry, so it can be read on its own; the rest only hold what changed since the entry before.
            var full = _previousLeaves is null || _previousFile != file || !File.Exists(file);
            string detail;
            if (full)
            {
                record["entryType"] = "full";
                foreach (var (key, value) in bodyNode.ToList())
                {
                    bodyNode.Remove(key);
                    record[key] = value;
                }
                detail = "full data";
            }
            else
            {
                var changes = new JsonObject();
                foreach (var (path, json) in leaves)
                {
                    if (!_previousLeaves!.TryGetValue(path, out var old) || old != json)
                    {
                        changes[path] = JsonNode.Parse(json);
                    }
                }

                var removed = _previousLeaves!.Keys.Where(path => !leaves.ContainsKey(path)).ToList();
                record["entryType"] = "changes";
                record["changes"] = changes;
                if (removed.Count > 0)
                {
                    record["removed"] = new JsonArray(removed.Select(path => (JsonNode?)path).ToArray());
                }
                detail = $"{changes.Count} change(s), {removed.Count} removed";
            }

            AppendToFile(file, record.ToJsonString(JsonOptions));
            _previousFile = file;
            _previousLeaves = leaves;
            _logger.LogInformation("[Data] Saved {Source} ({Count} issue(s), {Detail}) to {File}", entry.Source, entry.Issues.Count, detail, Path.GetRelativePath(DataFolder, file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not write the issue data file");
        }
    }

    /// <summary>Maps every leaf value to its path, e.g. "system.cpuUsagePercent" or "sensors[3].value", as raw JSON.</summary>
    private static void Flatten(JsonNode? node, string path, Dictionary<string, string> leaves)
    {
        switch (node)
        {
            case JsonObject obj when obj.Count > 0:
                foreach (var (key, child) in obj)
                {
                    Flatten(child, path.Length == 0 ? key : $"{path}.{key}", leaves);
                }
                break;
            case JsonArray array when array.Count > 0:
                for (var i = 0; i < array.Count; i++)
                {
                    Flatten(array[i], $"{path}[{i}]", leaves);
                }
                break;
            default:
                leaves[path] = node?.ToJsonString() ?? "null";
                break;
        }
    }

    private async Task<object?> Safe<T>(Func<Task<T>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Issue data: one source failed");
            return null;
        }
    }

    /// <summary>Keeps the file a valid JSON array: replaces the closing bracket with ", entry ]".</summary>
    private static void AppendToFile(string path, string entryJson)
    {
        var indented = "  " + entryJson.ReplaceLineEndings(Environment.NewLine + "  ");
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            MonitorAgent.Shared.Security.PrivateFile.WriteAllText(path, $"[{Environment.NewLine}{indented}{Environment.NewLine}]{Environment.NewLine}");
            return;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        var closing = FindLast(stream, stream.Length - 1, b => b == (byte)']');
        var previous = closing > 0 ? FindLast(stream, closing - 1, b => !char.IsWhiteSpace((char)b)) : -1;
        if (previous < 0)
        {
            stream.Dispose();
            File.Move(path, Path.ChangeExtension(path, $".damaged-{DateTime.Now:HHmmss}.json"));
            AppendToFile(path, entryJson);
            return;
        }

        stream.Seek(previous, SeekOrigin.Begin);
        var separator = stream.ReadByte() == '[' ? string.Empty : ",";

        stream.SetLength(previous + 1);
        stream.Seek(0, SeekOrigin.End);
        var bytes = new UTF8Encoding(false).GetBytes($"{separator}{Environment.NewLine}{indented}{Environment.NewLine}]{Environment.NewLine}");
        stream.Write(bytes);
    }

    private static long FindLast(FileStream stream, long from, Func<int, bool> match)
    {
        for (var position = from; position >= 0; position--)
        {
            stream.Seek(position, SeekOrigin.Begin);
            if (match(stream.ReadByte()))
            {
                return position;
            }
        }

        return -1;
    }

    private sealed record PendingEntry(DateTime At, string Source, List<AgentIssueDto> Issues, bool Resolved = false);
}
