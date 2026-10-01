using System.Text.Json;
using MonitorAgent.Service.Config;
using MonitorAgent.Service.SystemInfo;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Monitoring;

namespace MonitorAgent.Service.Monitoring;

public interface INotificationStore
{
    IReadOnlyList<AgentIssueDto> GetNotifications();

    /// <summary>Shows a message (e.g. "Success" for a settings change) at the top of the notifications for a few seconds.</summary>
    void ShowTemporary(AgentIssueDto message, TimeSpan duration);
}

/// <summary>
/// Builds the warnings / problems shown in the app's notifications, on the service side, so they are
/// detected and saved to the Data folder even when the app is closed.
/// </summary>
public sealed class NotificationEngine : BackgroundService, INotificationStore
{
    /// <summary>Temperatures and fans change slowly, and reading every sensor is the heaviest part of a round.</summary>
    private static readonly TimeSpan SensorsMaxAge = TimeSpan.FromSeconds(30);

    private readonly IServiceProvider _services;
    private readonly ILocalConfigCache _config;
    private readonly IInternetStatus _internet;
    private readonly IIssueDataLogger _dataLogger;
    private readonly IConnectivityFilter _connectivity;
    private readonly NotificationTrigger _trigger;
    private readonly ILogger<NotificationEngine> _logger;
    private readonly object _temporaryLock = new();
    private readonly List<(AgentIssueDto Message, DateTime ExpiresUtc)> _temporary = [];
    private IReadOnlyList<AgentIssueDto> _current = [];
    private IReadOnlyList<AgentIssueDto> _previousCheckIssues = [];
    private double _peakDownloadMbps;
    private double _peakUploadMbps;

    public NotificationEngine(
        IServiceProvider services,
        ILocalConfigCache config,
        IInternetStatus internet,
        IIssueDataLogger dataLogger,
        IConnectivityFilter connectivity,
        NotificationTrigger trigger,
        ILogger<NotificationEngine> logger)
    {
        _services = services;
        _config = config;
        _internet = internet;
        _dataLogger = dataLogger;
        _connectivity = connectivity;
        _trigger = trigger;
        _logger = logger;
    }

    public IReadOnlyList<AgentIssueDto> GetNotifications()
    {
        List<AgentIssueDto> temporary;
        lock (_temporaryLock)
        {
            _temporary.RemoveAll(item => item.ExpiresUtc <= DateTime.UtcNow);
            temporary = _temporary.Select(item => item.Message).Reverse().ToList();
        }

        var current = Volatile.Read(ref _current);
        return temporary.Count == 0 ? current : temporary.Concat(current).ToList();
    }

    public void ShowTemporary(AgentIssueDto message, TimeSpan duration)
    {
        lock (_temporaryLock)
        {
            _temporary.Add((message, DateTime.UtcNow + duration));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var general = _config.GetGeneral();
            try
            {
                // Always evaluated and saved; the Notifications setting only controls whether the app shows them.
                _logger.LogInformation("[Device checks] Reading CPU, RAM, disks, internet, hardware, OS and sensors...");
                var (notifications, checkIssues, allFound) = await EvaluateAsync(stoppingToken);
                Volatile.Write(ref _current, notifications);
                if (checkIssues.Count == 0)
                {
                    _logger.LogInformation("[Device checks] OK - everything meets Device Specifications");
                }

                foreach (var issue in checkIssues)
                {
                    _logger.LogWarning("[Device checks] {Severity} - {Title}", issue.Severity.ToUpperInvariant(), issue.Title);
                }

                _logger.LogInformation("[Device checks] {Count} notification(s) to show in the app", notifications.Count);
                _dataLogger.Record("Device checks", checkIssues);
                // Compared before the network filter, so an issue hidden while the internet is down is not taken as fixed.
                var resolved = _previousCheckIssues.Where(old => allFound.All(issue => issue.Id != old.Id)).ToList();
                foreach (var issue in resolved)
                {
                    _logger.LogInformation("[Device checks] RESOLVED - {Title}", issue.Title);
                }

                _dataLogger.RecordResolved("Device checks", resolved);
                _previousCheckIssues = allFound;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Building notifications failed");
            }

            // A monitor, the internet check or a settings change wakes this up right away; a short pause groups changes together.
            if (await _trigger.WaitAsync(TimeSpan.FromSeconds(Math.Max(2, general.RefreshInterval)), stoppingToken))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), stoppingToken);
            }
        }
    }

    /// <returns>The notifications to show, and every problem / warning found by this round of device checks
    /// (ignoring the Alert and Notify settings; monitor points are saved by their own checks).</returns>
    private async Task<(IReadOnlyList<AgentIssueDto> Notifications, IReadOnlyList<AgentIssueDto> CheckIssues, IReadOnlyList<AgentIssueDto> AllFound)> EvaluateAsync(
        CancellationToken cancellationToken)
    {
        var system = _services.GetRequiredService<ISystemInfoService>();
        var hardware = _services.GetRequiredService<IHardwareService>();
        var sensors = _services.GetRequiredService<ISensorsService>();

        var cpuTask = system.GetCpuAsync(cancellationToken);
        var ramTask = system.GetRamAsync(cancellationToken);
        var partitionsTask = system.GetPartitionsAsync(cancellationToken);
        var networkTask = system.GetNetworkAsync(cancellationToken);
        var osTask = hardware.GetOsAsync(cancellationToken);
        var level1Task = hardware.GetLevelAsync(1, cancellationToken);
        var level4Task = hardware.GetLevelAsync(4, cancellationToken);
        var sensorsTask = sensors.GetLevelAsync(SensorsMaxAge, cancellationToken);
        var pointsTask = MonitorPointStatusBuilder.BuildAsync(_services, cancellationToken);
        await Task.WhenAll(cpuTask, ramTask, partitionsTask, networkTask, osTask, level1Task, level4Task, sensorsTask, pointsTask);

        var cpu = cpuTask.Result;
        var ram = ramTask.Result;
        var network = networkTask.Result;
        var os = osTask.Result;
        var operatingSystem = $"{os.Name} {os.Version}".Trim();
        _peakDownloadMbps = Math.Max(_peakDownloadMbps, network.DownloadMbps);
        _peakUploadMbps = Math.Max(_peakUploadMbps, network.UploadMbps);

        var internet = _internet.GetState();
        var spec = _config.GetDeviceSpec();
        _logger.LogInformation(
            "[Device checks] CPU {Cpu:0.#}% ({Cores} cores), RAM {Ram:0.#}% of {RamGb:0.#} GB, disks {Disks}, internet {Internet}, speed {Down:0.##}/{Up:0.##} Mbps, OS {Os}",
            cpu.UsagePercent, cpu.LogicalCores, ram.UsagePercent, ram.TotalGB,
            string.Join(" ", partitionsTask.Result.Select(p => $"{p.DriveLetter.TrimEnd('\\')} {p.FreeGB:0.#}/{p.TotalGB:0.#} GB free")),
            internet.Connected switch { true => "connected", false => "disconnected", _ => "not checked yet" },
            internet.DownloadMbps ?? _peakDownloadMbps, internet.UploadMbps ?? _peakUploadMbps, operatingSystem);
        var reading = new DeviceSpecReading(
            cpu.LogicalCores,
            cpu.UsagePercent,
            ram.TotalGB,
            ram.UsagePercent,
            partitionsTask.Result.Select(p => new DiskSlice(p.DriveLetter.TrimEnd('\\'), p.TotalGB, p.FreeGB, p.UsagePercent)).ToList(),
            internet.Connected ?? true,
            internet.DownloadMbps ?? _peakDownloadMbps,
            internet.UploadMbps ?? _peakUploadMbps,
            operatingSystem);

        var hardwareRows = (level1Task.Result?.Items ?? []).Concat(level4Task.Result?.Items ?? []).ToList();
        var sensorIssues = NotificationRules.Sensors(sensorsTask.Result.Items ?? []).ToList();

        var result = DeviceSpecEvaluator.Evaluate(spec, reading);
        var found = NotificationRules.HardwareOs(spec, hardwareRows, result.OsBad, operatingSystem)
            .Concat(sensorIssues)
            .Concat(result.Issues)
            .Concat(NotificationRules.MonitorPoints(pointsTask.Result))
            .ToList();
        var notifications = (await _connectivity.RemoveNetworkDependentAsync(found, cancellationToken))
            .Concat(_internet.GetConnectivityIssues())
            .OrderBy(issue => SeverityRank(issue.Severity))
            .ToList();

        var everything = ReportEverything(spec);
        var allResult = DeviceSpecEvaluator.Evaluate(everything, reading);
        var allFound = NotificationRules.HardwareOs(everything, hardwareRows, allResult.OsBad, operatingSystem)
            .Concat(sensorIssues)
            .Concat(allResult.Issues)
            .Where(issue => issue.Severity is "Critical" or "Warning")
            .ToList();
        var checkIssues = (await _connectivity.RemoveNetworkDependentAsync(allFound, cancellationToken))
            .OrderBy(issue => SeverityRank(issue.Severity))
            .ToList();

        return (notifications, checkIssues, allFound);
    }

    /// <summary>A copy of the settings that reports every warning, whatever the Alert / Notify choices are.</summary>
    private static DeviceSpecSettings ReportEverything(DeviceSpecSettings spec)
    {
        var copy = JsonSerializer.Deserialize<DeviceSpecSettings>(JsonSerializer.Serialize(spec)) ?? new DeviceSpecSettings();
        copy.CpuAlert = MonitorPointAlert.Warning;
        copy.RamAlert = MonitorPointAlert.Warning;
        copy.DiskAlert = MonitorPointAlert.Warning;
        copy.InternetNotify = true;
        copy.DownloadNotify = true;
        copy.UploadNotify = true;
        copy.OsNotify = true;
        return copy;
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "Critical" => 0,
        "Warning" => 1,
        _ => 2
    };
}
