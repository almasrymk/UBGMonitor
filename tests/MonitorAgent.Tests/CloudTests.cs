using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MonitorAgent.Cloud;
using MonitorCloud.AgentProtocol.V1;

namespace MonitorAgent.Tests;

/// <summary>The Monitor Cloud connector (AG-12): protocol file, aggregator maths, outbox, issue tracking, inventory, back-off.</summary>
public sealed class CloudTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "monitoragent-cloud-tests", Guid.NewGuid().ToString("N"));

    public CloudTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may still hold the file for a moment; the temp folder is cleaned later.
        }
    }

    private Outbox NewOutbox(long cap = 200L * 1024 * 1024, string name = "cloud.db") => new(Path.Combine(_folder, name), cap);

    private static CloudSample Sample(DateTimeOffset at, double cpu, double ram = 50, double disk = 40) => new(at, cpu, ram, disk, 3600);

    private static readonly DateTimeOffset Minute = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------- AG-4

    [Fact]
    public void The_protocol_file_matches_its_recorded_hash()
    {
        var root = RepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "proto", "monitor", "agent", "v1", "agent.proto")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var recorded = File.ReadAllLines(Path.Combine(root, "proto", "VERSION"))[0].Split(' ')[^1];

        Assert.Equal(recorded, hash);
    }

    // ---------------------------------------------------------------- AG-6

    [Fact]
    public void P95_uses_the_nearest_rank()
    {
        var values = Enumerable.Range(1, 20).Select(i => (double)i).ToList();

        Assert.Equal(19, MinuteAggregator.P95(values));
        Assert.Equal(7, MinuteAggregator.P95([7]));
        Assert.Equal(10, MinuteAggregator.P95([1, 2, 10]));
    }

    [Fact]
    public void A_minute_is_completed_by_the_first_sample_of_the_next_minute()
    {
        var aggregator = new MinuteAggregator();

        Assert.Null(aggregator.Add(Sample(Minute.AddSeconds(5), 10, ram: 40, disk: 50)));
        Assert.Null(aggregator.Add(Sample(Minute.AddSeconds(25), 30, ram: 60, disk: 70)));
        Assert.Null(aggregator.Add(Sample(Minute.AddSeconds(45), 20, ram: 50, disk: 60) with { TempC = 61, RxBps = 1000 }));
        var minute = aggregator.Add(Sample(Minute.AddSeconds(65), 99));

        Assert.NotNull(minute);
        Assert.Equal(Minute, minute.BucketStart.ToDateTimeOffset());
        Assert.Equal(3u, minute.Samples);
        Assert.Equal(20, minute.CpuAvg);
        Assert.Equal(30, minute.CpuMax);
        Assert.Equal(30, minute.CpuP95);
        Assert.Equal(50, minute.RamAvg);
        Assert.Equal(60, minute.RamMax);
        Assert.Equal(70, minute.DiskPercentMax);
        Assert.Equal(61, minute.TempMaxC);
        Assert.Equal(1000ul, minute.NetRxBps);
        Assert.False(minute.HasDiskActiveAvg);
    }

    [Fact]
    public void Samples_of_an_older_minute_are_ignored_and_values_are_clamped()
    {
        var aggregator = new MinuteAggregator();
        aggregator.Add(Sample(Minute.AddMinutes(1), 150));
        Assert.Null(aggregator.Add(Sample(Minute.AddSeconds(30), 10)));

        var minute = aggregator.Add(Sample(Minute.AddMinutes(2), 5));

        Assert.NotNull(minute);
        Assert.Equal(1u, minute.Samples);
        Assert.Equal(100, minute.CpuMax);
        Assert.Throws<ArgumentException>(() => MinuteAggregator.Build(Minute, []));
    }

    // ---------------------------------------------------------------- AG-5

    [Fact]
    public void The_outbox_keeps_order_returns_what_is_pending_and_deletes_acknowledged_rows()
    {
        using var outbox = NewOutbox();
        var first = outbox.Enqueue(Outbox.Metric, new AgentMessage { MetricBatch = new MetricBatch() });
        var second = outbox.Enqueue(Outbox.Issue, new AgentMessage { Issue = new IssueEvent { IssueKey = "cpu" } });
        var third = outbox.Enqueue(Outbox.Inventory, new AgentMessage { Inventory = new InventoryUpdate { Hash = "h" } });

        Assert.True(first < second && second < third);
        var pending = outbox.Pending(first);
        Assert.Equal([second, third], pending.Select(p => p.Sequence));
        Assert.Equal((ulong)second, pending[0].Message.Sequence);
        Assert.Equal("cpu", pending[0].Message.Issue.IssueKey);
        Assert.False(string.IsNullOrEmpty(pending[0].Message.MessageId));

        Assert.Equal(2, outbox.Acknowledge(second));
        Assert.Equal(1, outbox.Depth);
        Assert.Equal(second, outbox.LastAcknowledged);
    }

    [Fact]
    public void The_outbox_survives_a_restart()
    {
        long sequence;
        using (var outbox = NewOutbox(name: "restart.db"))
        {
            outbox.Enqueue(Outbox.Metric, new AgentMessage { MetricBatch = new MetricBatch() });
            sequence = outbox.Enqueue(Outbox.Issue, new AgentMessage { Issue = new IssueEvent { IssueKey = "ram" } });
            outbox.Acknowledge(sequence - 1);
        }

        using var reopened = NewOutbox(name: "restart.db");
        Assert.Equal(sequence - 1, reopened.LastAcknowledged);
        Assert.Equal([sequence], reopened.Pending(0).Select(p => p.Sequence));
        Assert.True(reopened.Enqueue(Outbox.Metric, new AgentMessage { MetricBatch = new MetricBatch() }) > sequence);
    }

    [Fact]
    public void Above_the_cap_the_oldest_metric_rows_go_first_and_issues_are_kept()
    {
        using var outbox = NewOutbox(cap: 20_000);
        var issue = outbox.Enqueue(Outbox.Issue, new AgentMessage { Issue = new IssueEvent { IssueKey = "disk-C", Message = new string('x', 2000) } });
        for (var i = 0; i < 40; i++)
            outbox.Enqueue(Outbox.Metric, new AgentMessage { MetricBatch = new MetricBatch { Disks = { new DiskUsage { Drive = new string('d', 1000) } } } });

        Assert.True(outbox.Bytes <= 20_000);
        var kinds = outbox.Pending(0, 1000);
        Assert.Contains(kinds, r => r.Sequence == issue && r.Kind == Outbox.Issue);
        Assert.True(kinds.Count(r => r.Kind == Outbox.Metric) < 40);
        // The newest metric row is always kept.
        Assert.Equal(Outbox.Metric, kinds[^1].Kind);
    }

    // ---------------------------------------------------------------- issues

    [Fact]
    public void Issue_changes_are_raised_changed_and_cleared_once()
    {
        using var outbox = NewOutbox();
        var tracker = new IssueChangeTracker(outbox);
        var cpu = new CloudIssue("cpu", "Critical", "CPU threshold", "CPU 95%", Minute);

        var raised = tracker.Changes([cpu, new CloudIssue("disk-C", "Warning", "Disk", "Disk 91%", Minute)], Minute);
        Assert.Equal([IssueAction.Raised, IssueAction.Raised], raised.Select(e => e.Action));
        Assert.Equal("Performance", raised.Single(e => e.IssueKey == "cpu").Category);
        Assert.Equal("Storage", raised.Single(e => e.IssueKey == "disk-C").Category);
        Assert.Equal(Severity.Critical, raised[0].Severity);

        Assert.Empty(tracker.Changes([cpu, new CloudIssue("disk-C", "Warning", "Disk", "Disk 92%", Minute)], Minute.AddSeconds(5)));

        var changed = tracker.Changes([cpu with { Severity = "Warning" }, new CloudIssue("disk-C", "Warning", "Disk", "Disk 92%", Minute)], Minute.AddSeconds(10));
        Assert.Equal(IssueAction.SeverityChanged, Assert.Single(changed).Action);

        var cleared = new IssueChangeTracker(outbox).Changes([], Minute.AddSeconds(15));
        Assert.Equal(["cpu", "disk-C"], cleared.Select(e => e.IssueKey).Order());
        Assert.All(cleared, e => Assert.Equal(IssueAction.Cleared, e.Action));
    }

    [Fact]
    public void Cloud_issue_keys_are_never_reported_by_the_agent()
    {
        using var outbox = NewOutbox();

        var events = new IssueChangeTracker(outbox).Changes([new CloudIssue("device-offline", "Critical", "x", "y", Minute), new CloudIssue("license", "Warning", "x", "y", Minute)], Minute);

        Assert.Empty(events);
        Assert.Equal("Service", IssueChangeTracker.CategoryOf("web-shop", "web-shop"));
        Assert.Equal("Connectivity", IssueChangeTracker.CategoryOf("internet", null));
        Assert.Equal(Severity.Info, IssueChangeTracker.SeverityOf("whatever"));
    }

    // ---------------------------------------------------------------- inventory, payloads, back-off, state

    [Fact]
    public void Inventory_is_queued_only_when_its_hash_changes()
    {
        using var outbox = NewOutbox();
        var publisher = new InventoryPublisher(outbox);

        Assert.Equal(2, publisher.Publish(new Dictionary<string, object> { ["hardware"] = new { model = "A" }, ["os"] = new { name = "Windows" }, ["unknown"] = 1 }));
        Assert.Equal(0, publisher.Publish(new Dictionary<string, object> { ["hardware"] = new { model = "A" } }));
        Assert.Equal(1, publisher.Publish(new Dictionary<string, object> { ["hardware"] = new { model = "B" } }));

        var update = outbox.Pending(0).Last().Message.Inventory;
        Assert.Equal(InventoryKind.Hardware, update.Kind);
        Assert.Equal("{\"model\":\"B\"}", Payloads.Decompress(update.JsonBrotli));
        Assert.Equal(Payloads.Sha256("{\"model\":\"B\"}"), update.Hash);
    }

    [Fact]
    public void Reconnect_backs_off_to_five_minutes_with_jitter_and_honours_retry_after()
    {
        var policy = new ReconnectPolicy(new Random(1));
        var delays = Enumerable.Range(0, 12).Select(_ => policy.Next()).ToList();

        Assert.InRange(delays[0].TotalSeconds, 0.8, 1.2);
        Assert.InRange(delays[1].TotalSeconds, 1.6, 2.4);
        Assert.All(delays, d => Assert.True(d <= TimeSpan.FromMinutes(6)));
        Assert.InRange(delays[^1].TotalSeconds, 240, 360);
        Assert.Equal(TimeSpan.FromSeconds(17), policy.Next(17));
        Assert.InRange(policy.Next().TotalSeconds, 0.8, 1.2);
        policy.Next();
        policy.Reset();
        Assert.InRange(policy.Next().TotalSeconds, 0.8, 1.2);
    }

    [Fact]
    public void The_enrollment_state_keeps_the_secret_encrypted()
    {
        var store = new CloudStateStore(_folder);
        Assert.False(store.Load().IsEnrolled);

        var state = CloudStateStore.Enrolled(new CloudState(), new EnrollmentResult(Guid.NewGuid(), "TEST-ONLY-device-secret", "http://localhost:5301", "Acme", "Cairo HQ", "Licensed", null, null));
        store.Save(state);

        var loaded = store.Load();
        Assert.True(loaded.IsEnrolled);
        Assert.Equal("TEST-ONLY-device-secret", CloudStateStore.Secret(loaded));
        Assert.DoesNotContain("TEST-ONLY-device-secret", File.ReadAllText(Path.Combine(_folder, "cloud.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Monitor_points_are_reported_in_full_after_hello_and_then_only_when_they_change()
    {
        using var outbox = NewOutbox();
        var hashes = new Dictionary<string, string>();
        var point = new CloudPoint("shop", "Shop", "Website", "https://shop.example.test", true, "Healthy", "OK", 42, Minute, Minute, 60);

        CloudAgentService.QueuePoints(outbox, [point], hashes, full: true);
        CloudAgentService.QueuePoints(outbox, [point with { LastChecked = Minute.AddSeconds(30) }], hashes, full: false);
        CloudAgentService.QueuePoints(outbox, [point with { Status = "Critical" }], hashes, full: false);

        var reports = outbox.Pending(0).Select(r => r.Message.MonitorPoints).ToList();
        Assert.Equal(2, reports.Count);
        Assert.True(reports[0].Full);
        Assert.Equal(PointStatus.PointCritical, Assert.Single(reports[1].Points).Status);
        Assert.Equal(PointStatus.PointUnknown, CloudAgentService.PointStatusOf("Paused"));
    }

    [Fact]
    public void Options_come_from_the_installer_environment()
    {
        Environment.SetEnvironmentVariable("MONITORAGENT_CLOUDURL", "https://cloud.example.test");
        Environment.SetEnvironmentVariable("MONITORAGENT_LOCATION", "LOC-TEST01-TEST02");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Cloud:SampleSeconds"] = "99" }).Build();
            var options = CloudOptions.FromConfiguration(configuration, _folder);

            Assert.True(options.Enabled);
            Assert.Equal("https://cloud.example.test", options.BaseUrl);
            Assert.Equal("LOC-TEST01-TEST02", options.LocationCode);
            Assert.Equal(30, options.SampleSeconds);
            Assert.Equal(_folder, options.StateFolder);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MONITORAGENT_CLOUDURL", null);
            Environment.SetEnvironmentVariable("MONITORAGENT_LOCATION", null);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MonitorAgent.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("MonitorAgent.sln not found.");
    }
}

/// <summary>
/// End-to-end test against a running Monitor Cloud (AG-12, MC-701). Runs when MONITORCLOUD_E2E_URL,
/// MONITORCLOUD_E2E_GATEWAY and MONITORCLOUD_E2E_PRODUCTKEY are set (a development cloud with demo data).
/// </summary>
public sealed class CloudEndToEndTests
{
    private sealed class E2EFactAttribute : FactAttribute
    {
        public E2EFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MONITORCLOUD_E2E_URL")))
                Skip = "Set MONITORCLOUD_E2E_URL, MONITORCLOUD_E2E_GATEWAY and MONITORCLOUD_E2E_PRODUCTKEY to run against a Monitor Cloud.";
        }
    }

    private sealed class FakeSource(string fingerprint) : ICloudAgentSource
    {
        public List<CloudIssue> Current { get; } = [];

        public string Fingerprint => fingerprint;

        public CloudHost Host() => new($"E2E-{fingerprint[^6..].ToUpperInvariant()}", "Windows", "Windows Server 2022", "10.0.20348", "x64", "1.1.0-e2e", "10.0.0.5", null, null);

        public Task<CloudSample> SampleAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CloudSample(DateTimeOffset.UtcNow, 20 + Random.Shared.Next(10), 40, 55, 3600, RxBps: 1000, TxBps: 500));

        public Task<IReadOnlyList<CloudDisk>> DisksAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CloudDisk>>([new CloudDisk("C:", "System", "NTFS", 100, 55, 45)]);

        public IReadOnlyList<CloudIssue> Issues() => Current.ToList();

        public IReadOnlyList<CloudPoint> MonitorPoints() => [];

        public Task<object?> SnapshotAsync(CancellationToken cancellationToken) => Task.FromResult<object?>(new { cpu = new { usage = 25 } });

        public Task<IReadOnlyDictionary<string, object>> InventoryAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, object>>(new Dictionary<string, object> { ["os"] = new { name = "Windows Server 2022" } });
    }

    [E2EFact]
    public async Task The_connector_enrolls_connects_and_gets_its_messages_acknowledged()
    {
        var folder = Path.Combine(Path.GetTempPath(), "monitoragent-e2e", Guid.NewGuid().ToString("N"));
        var options = new CloudOptions
        {
            Enabled = true,
            BaseUrl = Environment.GetEnvironmentVariable("MONITORCLOUD_E2E_URL"),
            GatewayUrl = Environment.GetEnvironmentVariable("MONITORCLOUD_E2E_GATEWAY"),
            ProductKey = Environment.GetEnvironmentVariable("MONITORCLOUD_E2E_PRODUCTKEY"),
            LocationCode = Environment.GetEnvironmentVariable("MONITORCLOUD_E2E_LOCATION"),
            SampleSeconds = 1,
            StateFolder = folder,
        };
        var source = new FakeSource("ma-e2e-" + Guid.NewGuid().ToString("N")[..20]);
        source.Current.Add(new CloudIssue("cpu", "Critical", "CPU threshold", "CPU 97% (e2e)", DateTimeOffset.UtcNow));
        var http = new HttpClient { BaseAddress = new Uri(options.BaseUrl!.TrimEnd('/') + "/") };
        var client = new CloudHttpClient(http);
        var status = new CloudStatus();
        var service = new CloudAgentService(
            Microsoft.Extensions.Options.Options.Create(options), source, new NoopLicense(), new AcceptAll(), new NoCommands(), new CloudStateStore(folder), client, new DeviceTokenProvider(client, TimeProvider.System),
            status, TimeProvider.System, NullLogger<CloudAgentService>.Instance);

        using var stop = new CancellationTokenSource();
        await service.StartAsync(stop.Token);
        try
        {
            // Connected, and the first minute (plus the issue and inventory) acknowledged: the outbox drains.
            await WaitAsync(() => status.State == "Connected", TimeSpan.FromSeconds(30));
            await WaitAsync(() => service.Outbox is { } o && o.LastAcknowledged > 0 && o.Depth == 0, TimeSpan.FromSeconds(120));
            Assert.True(new CloudStateStore(folder).Load().IsEnrolled);
        }
        finally
        {
            await stop.CancelAsync();
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static async Task WaitAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out.");
            await Task.Delay(500);
        }
    }

    private sealed class AcceptAll : ICloudConfigApplier
    {
        public Task<(bool Success, string? Error)> ApplyAsync(int version, string json, CancellationToken cancellationToken) => Task.FromResult((true, (string?)null));
    }

    private sealed class NoCommands : ICloudCommandExecutor
    {
        public Task<(bool Success, string Output)> ExecuteAsync(string type, string? service, CancellationToken cancellationToken) => Task.FromResult((false, "not in this test"));
    }

    private sealed class NoopLicense : ICloudLicenseSink
    {
        public void Update(string state, string? reasonCode, string? token, DateTimeOffset? checkAfter)
        {
        }
    }
}

public sealed class CloudConfigDocumentTests
{
    private const string Valid = """
        { "version": 7, "telemetry": { "sampleSeconds": 10 },
          "thresholds": { "cpu": { "warningPercent": 70, "criticalPercent": 90, "forSeconds": 60, "clearBelowPercent": 65 },
                          "ram": { "warningPercent": 80, "criticalPercent": 95, "forSeconds": 300 },
                          "disk": { "warningPercent": 85, "criticalPercent": 92, "forSeconds": 60 },
                          "tempC": { "critical": 85, "forSeconds": 120 } },
          "monitorPoints": [], "features": { "remoteActions": false } }
        """;

    [Fact]
    public void A_valid_document_is_read()
    {
        Assert.True(CloudConfigDocument.TryParse(Valid, out var document, out var error));
        Assert.Null(error);
        Assert.Equal(7, document!.Version);
        Assert.Equal(10, document.SampleSeconds);
        Assert.Equal(90, document.Cpu.CriticalPercent);
        Assert.Equal(65, document.Cpu.ClearBelowPercent);
        Assert.Null(document.Ram.ClearBelowPercent);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{ \"thresholds\": {} }")]
    [InlineData("{ \"thresholds\": { \"cpu\": { \"warningPercent\": 90, \"criticalPercent\": 80 }, \"ram\": { \"warningPercent\": 80, \"criticalPercent\": 95 }, \"disk\": { \"warningPercent\": 85, \"criticalPercent\": 92 } } }")]
    public void An_invalid_document_is_refused_with_a_reason(string json)
    {
        Assert.False(CloudConfigDocument.TryParse(json, out var document, out var error));
        Assert.Null(document);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
