using System.Diagnostics;
using MonitorAgent.Service.Config;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Monitoring;

public sealed class ApplicationMonitor : BackgroundService, IMonitoringModule
{
    private readonly ILocalConfigCache _configCache;
    private readonly IMonitorHealthStore _health;
    private readonly ILogger<ApplicationMonitor> _logger;
    private readonly Dictionary<string, bool> _lastUp = new(StringComparer.OrdinalIgnoreCase);

    public ApplicationMonitor(
        ILocalConfigCache configCache,
        IMonitorHealthStore health,
        ILogger<ApplicationMonitor> logger)
    {
        _configCache = configCache;
        _health = health;
        _logger = logger;
    }

    public string Name => nameof(ApplicationMonitor);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Application monitor cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var config = await _configCache.GetConfigAsync(cancellationToken);
        var points = config.MonitorPoints
            .Where(point => point.Enabled && point.Type == MonitorPointType.Application)
            .ToList();

        var active = new HashSet<string>(points.Select(point => point.MonitorPointId), StringComparer.OrdinalIgnoreCase);
        foreach (var id in _lastUp.Keys.Where(id => !active.Contains(id)).ToList())
        {
            _health.ClearIssue(IssueKey(id));
            _lastUp.Remove(id);
        }

        if (points.Count == 0)
        {
            return;
        }

        _logger.LogInformation("[Application] Checking {Count} application(s) against the running processes...", points.Count);
        var running = RunningProcessNames();
        foreach (var point in points)
        {
            var processName = ProcessName(point);
            var up = !string.IsNullOrWhiteSpace(processName) && running.Contains(processName);
            var message = up
                ? $"Application is running ({processName})"
                : string.IsNullOrWhiteSpace(processName)
                    ? "Application executable was not found"
                    : $"Application is not running ({processName})";

            _health.SetPointHealth(point.MonitorPointId, up, message);
            if (up)
            {
                _health.ClearIssue(IssueKey(point.MonitorPointId));
            }
            else
            {
                _health.SetIssue(
                    IssueKey(point.MonitorPointId),
                    "Critical",
                    "Application not running",
                    IssueText.ApplicationDown(point.DisplayName, processName),
                    point.MonitorPointId);
            }

            if (up)
            {
                _logger.LogInformation("[Application] {Name}: OK - {Message}", point.DisplayName, message);
            }
            else
            {
                _logger.LogWarning("[Application] {Name}: CRITICAL - {Message}", point.DisplayName, message);
            }

            _lastUp[point.MonitorPointId] = up;
        }
    }

    private static HashSet<string> RunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(process.ProcessName))
                    {
                        names.Add(process.ProcessName);
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return names;
    }

    private static string ProcessName(MonitorPoint point)
    {
        if (!string.IsNullOrWhiteSpace(point.Model))
        {
            return point.Model.Trim();
        }

        return string.IsNullOrWhiteSpace(point.Address)
            ? string.Empty
            : Path.GetFileNameWithoutExtension(point.Address.Trim());
    }

    private static string IssueKey(string monitorPointId) => $"application:{monitorPointId}";
}
