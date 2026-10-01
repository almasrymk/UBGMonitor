using System.Net.NetworkInformation;
using MonitorAgent.Service.Config;
using MonitorAgent.Service.Reports;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Monitoring;

namespace MonitorAgent.Service.Monitoring;

public enum ConnectivityProblem
{
    None,
    /// <summary>The device itself is not connected to any network (cable, Wi-Fi, disabled adapter, driver).</summary>
    Network,
    /// <summary>The device is on its network, but the internet cannot be reached.</summary>
    Internet
}

public interface IInternetStatus
{
    InternetStateDto GetState();

    ConnectivityProblem Problem { get; }

    /// <summary>The one network or internet problem to report, plus a network driver warning if there is one.</summary>
    IReadOnlyList<AgentIssueDto> GetConnectivityIssues();

    /// <summary>Checks the network and internet again now, unless that was done in the last few seconds.</summary>
    Task RecheckAsync(CancellationToken cancellationToken);

    /// <summary>Starts a speed test now unless one is already running.</summary>
    void StartSpeedTest();
}

/// <summary>Checks internet reachability and runs the scheduled speed tests, whether or not the app is open.</summary>
public sealed class InternetMonitor : BackgroundService, IInternetStatus
{
    private static readonly HttpClient ConnectivityClient = new() { Timeout = TimeSpan.FromSeconds(4) };
    private static readonly TimeSpan FirstSpeedTestDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RecheckAge = TimeSpan.FromSeconds(3);

    /// <summary>A test fills the line for about 16 seconds, so a shorter gap would leave one running all the time.</summary>
    private const int MinSpeedTestIntervalSeconds = 300;

    private readonly ILocalConfigCache _config;
    private readonly IIssueDataLogger _dataLogger;
    private readonly NotificationTrigger _trigger;
    private readonly ReportStore _reports;
    private readonly ILogger<InternetMonitor> _logger;
    private readonly object _lock = new();
    private readonly InternetStateDto _state = new();
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private DateTime _lastCheckUtc = DateTime.MinValue;
    private ConnectivityProblem _problem;
    private IReadOnlyList<AgentIssueDto> _connectivityIssues = [];
    private DateTime? _lastSpeedTestUtc;
    private int _speedTestRunning;
    private AgentIssueDto? _speedTestFailure;

    public InternetMonitor(
        ILocalConfigCache config, IIssueDataLogger dataLogger, NotificationTrigger trigger, ReportStore reports, ILogger<InternetMonitor> logger)
    {
        _config = config;
        _dataLogger = dataLogger;
        _trigger = trigger;
        _reports = reports;
        _logger = logger;
    }

    public InternetStateDto GetState()
    {
        lock (_lock)
        {
            return new InternetStateDto
            {
                Connected = _state.Connected,
                CheckedUtc = _state.CheckedUtc,
                SpeedTestRunning = _state.SpeedTestRunning,
                SpeedTestProgress = _state.SpeedTestProgress,
                DownloadMbps = _state.DownloadMbps,
                UploadMbps = _state.UploadMbps,
                SpeedTestCompletedAt = _state.SpeedTestCompletedAt,
                SpeedTestError = _state.SpeedTestError
            };
        }
    }

    public ConnectivityProblem Problem
    {
        get
        {
            lock (_lock)
            {
                return _problem;
            }
        }
    }

    public IReadOnlyList<AgentIssueDto> GetConnectivityIssues()
    {
        lock (_lock)
        {
            return _connectivityIssues;
        }
    }

    public async Task RecheckAsync(CancellationToken cancellationToken)
    {
        await _checkLock.WaitAsync(cancellationToken);
        try
        {
            if (DateTime.UtcNow - _lastCheckUtc >= RecheckAge)
            {
                await CheckAsync(cancellationToken);
            }
        }
        finally
        {
            _checkLock.Release();
        }
    }

    public void StartSpeedTest() => _ = RunSpeedTestAsync(CancellationToken.None);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var general = _config.GetGeneral();
            IReadOnlyList<AgentIssueDto> issues;
            await _checkLock.WaitAsync(stoppingToken);
            try
            {
                issues = await CheckAsync(stoppingToken);
            }
            finally
            {
                _checkLock.Release();
            }

            if (issues.Count > 0)
            {
                _dataLogger.Record(Problem == ConnectivityProblem.Internet ? "Internet check" : "Network check", issues);
            }

            var connected = Problem == ConnectivityProblem.None;
            var interval = general.SpeedTestIntervalSeconds > 0 ? Math.Max(MinSpeedTestIntervalSeconds, general.SpeedTestIntervalSeconds) : 0;
            var now = DateTime.UtcNow;
            if (interval > 0 && connected
                && now >= (_lastSpeedTestUtc?.AddSeconds(interval) ?? _startedUtc + FirstSpeedTestDelay))
            {
                _ = RunSpeedTestAsync(stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(general.Seconds(general.InternetIntervalSeconds, 5)), stoppingToken);
        }
    }

    private async Task RunSpeedTestAsync(CancellationToken stoppingToken)
    {
        if (Interlocked.Exchange(ref _speedTestRunning, 1) == 1)
        {
            return;
        }

        lock (_lock)
        {
            _state.SpeedTestRunning = true;
            _state.SpeedTestError = null;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            _logger.LogInformation("[Speed test] Starting the internet speed test...");
            var progress = new Progress<string>(text =>
            {
                lock (_lock)
                {
                    _state.SpeedTestProgress = text;
                }

                _logger.LogInformation("[Speed test] {Progress}", text);
            });
            var result = await InternetSpeedTester.RunAsync(progress, timeout.Token);
            _logger.LogInformation("[Speed test] OK - download {Download:0.00} Mbps, upload {Upload:0.00} Mbps",
                result.DownloadMbps, result.UploadMbps);
            lock (_lock)
            {
                _state.DownloadMbps = result.DownloadMbps;
                _state.UploadMbps = result.UploadMbps;
                _state.SpeedTestCompletedAt = result.CompletedAt;
            }

            _reports.AddSpeedTest(DateTime.UtcNow, result.DownloadMbps, result.UploadMbps, null);
            if (Interlocked.Exchange(ref _speedTestFailure, null) is { } failure)
            {
                _dataLogger.RecordResolved("Internet speed test", [failure]);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            _logger.LogWarning("[Speed test] WARNING - failed: {Error}", ex.Message);
            lock (_lock)
            {
                _state.SpeedTestError = ex.Message;
            }

            var failure = Issue("internet:speedtest", "Warning", "Internet speed test failed",
                $"The internet speed test could not finish: {ex.Message}");
            _speedTestFailure = failure;
            _reports.AddSpeedTest(DateTime.UtcNow, null, null, ex.Message);
            _dataLogger.Record("Internet speed test", [failure]);
        }
        finally
        {
            lock (_lock)
            {
                _state.SpeedTestRunning = false;
                _state.SpeedTestProgress = null;
            }

            _lastSpeedTestUtc = DateTime.UtcNow;
            Interlocked.Exchange(ref _speedTestRunning, 0);
        }
    }

    /// <summary>The device's network adapters first; the internet is only probed when the device is on a network.</summary>
    private async Task<IReadOnlyList<AgentIssueDto>> CheckAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Network] Checking the network adapters...");
        var report = NetworkAdapterCheck.Check();
        var issues = new List<AgentIssueDto>();
        ConnectivityProblem problem;
        if (!report.Connected)
        {
            var reasons = report.Problems.Concat(report.DriverProblems).ToList();
            _logger.LogWarning("[Network] CRITICAL - not connected to any network: {Reasons}", string.Join("; ", reasons));
            issues.Add(Issue("network:connection", "Critical", "No network connection",
                "This device is not connected to any network, so the internet and every check that needs the network " +
                "(monitor points, speed test) are not reported until it reconnects. " +
                $"Details: {string.Join(". ", reasons)}. " +
                "Plug in the network cable, turn on Wi-Fi and connect to a network, or enable the adapter in Network Connections. " +
                "If an adapter has a driver problem, reinstall its driver from Device Manager."));
            problem = ConnectivityProblem.Network;
        }
        else
        {
            _logger.LogInformation("[Network] OK - connected to a network");
            _logger.LogInformation("[Internet] Checking the internet connection...");
            if (await ProbeAsync(cancellationToken))
            {
                problem = ConnectivityProblem.None;
            }
            else
            {
                issues.Add(Issue("internet:connection", "Critical", "Internet disconnected",
                    "This device is connected to its network, but the internet cannot be reached: 1.1.1.1 and 8.8.8.8 did not answer ping " +
                    "and the connectivity test websites could not be reached. Checks that need the network (monitor points, speed test) " +
                    "are not reported until the internet is back. Check the router or contact the internet provider."));
                problem = ConnectivityProblem.Internet;
            }

            if (report.DriverProblems.Count > 0)
            {
                _logger.LogWarning("[Network] WARNING - network driver problem: {Problems}", string.Join("; ", report.DriverProblems));
                issues.Add(Issue("network:driver", "Warning", "Network adapter driver problem",
                    $"{string.Join(". ", report.DriverProblems)}. Reinstall or update the driver from Device Manager."));
            }
        }

        List<AgentIssueDto> resolved;
        bool changed;
        lock (_lock)
        {
            // An ongoing problem keeps the time it started.
            issues = issues.Select(issue => _connectivityIssues.FirstOrDefault(old => old.Id == issue.Id) is { } old
                    ? new AgentIssueDto { Id = issue.Id, Severity = issue.Severity, Title = issue.Title, Message = issue.Message, TimestampUtc = old.TimestampUtc }
                    : issue)
                .ToList();
            resolved = _connectivityIssues.Where(old => issues.All(issue => issue.Id != old.Id)).ToList();
            changed = problem != _problem || resolved.Count > 0 || issues.Count != _connectivityIssues.Count;
            _state.Connected = problem == ConnectivityProblem.None;
            _state.CheckedUtc = DateTime.UtcNow;
            _problem = problem;
            _connectivityIssues = issues;
        }

        _lastCheckUtc = DateTime.UtcNow;
        foreach (var group in resolved.GroupBy(issue => issue.Id.StartsWith("network:", StringComparison.Ordinal) ? "Network check" : "Internet check"))
        {
            _logger.LogInformation("[{Check}] RESOLVED - {Titles}", group.Key, string.Join(", ", group.Select(issue => issue.Title)));
            _dataLogger.RecordResolved(group.Key, group.ToList());
        }

        if (changed)
        {
            _trigger.Request();
        }

        return issues;
    }

    /// <summary>Ping first; if ICMP is blocked or drops, an HTTP connectivity check decides.</summary>
    private async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        foreach (var host in new[] { "1.1.1.1", "8.8.8.8" })
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host, 1500);
                if (reply.Status == IPStatus.Success)
                {
                    _logger.LogInformation("[Internet] OK - ping {Host} replied in {Ms} ms", host, reply.RoundtripTime);
                    return true;
                }

                _logger.LogInformation("[Internet] Ping {Host}: {Status}", host, reply.Status);
            }
            catch (Exception ex) when (ex is PingException or InvalidOperationException)
            {
                _logger.LogInformation("[Internet] Ping {Host} failed: {Error}", host, ex.Message);
            }
        }

        foreach (var url in new[] { "http://www.msftconnecttest.com/connecttest.txt", "http://clients3.google.com/generate_204" })
        {
            try
            {
                using var response = await ConnectivityClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[Internet] OK - {Url} answered HTTP {Code}", url, (int)response.StatusCode);
                    return true;
                }

                _logger.LogInformation("[Internet] {Url} answered HTTP {Code}", url, (int)response.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogInformation("[Internet] {Url} failed: {Error}", url, ex.Message);
            }
        }

        _logger.LogWarning("[Internet] CRITICAL - internet is not reachable");
        return false;
    }

    private static AgentIssueDto Issue(string id, string severity, string title, string message) => new()
    {
        Id = id,
        Severity = severity,
        Title = title,
        Message = message,
        TimestampUtc = DateTime.UtcNow
    };
}
