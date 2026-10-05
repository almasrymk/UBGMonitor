using MonitorAgent.Service.Config;
using MonitorAgent.Service.Platform;
using MonitorAgent.Service.Reports;
using MonitorAgent.Service.SystemInfo;
using MonitorAgent.Shared.Models;

namespace MonitorAgent.Service.Monitoring;

/// <summary>
/// Reads the installed programs, user accounts and services as often as General settings say, even when the app is closed,
/// and saves every change (to the Applications Changes report and the Data folder) and shows it as a notification.
/// A service that breaks is also a problem in the Problems &amp; Warnings report until it recovers.
/// </summary>
public sealed class ApplicationsWatcher : BackgroundService
{
    private const string ServicesSource = "Services check";
    private const int ShownInNotification = 5;
    private static readonly TimeSpan NotificationDuration = TimeSpan.FromSeconds(60);

    private readonly IApplicationsService _applications;
    private readonly ILocalConfigCache _config;
    private readonly ReportStore _store;
    private readonly IIssueDataLogger _dataLogger;
    private readonly INotificationStore _notifications;
    private readonly NotificationTrigger _trigger;
    private readonly ILogger<ApplicationsWatcher> _logger;
    private IReadOnlyList<InstalledProgram>? _programs;
    private IReadOnlyList<UserAccountDto>? _users;
    private IReadOnlyList<SystemServiceDto>? _services;

    public ApplicationsWatcher(
        IApplicationsService applications,
        ILocalConfigCache config,
        ReportStore store,
        IIssueDataLogger dataLogger,
        INotificationStore notifications,
        NotificationTrigger trigger,
        ILogger<ApplicationsWatcher> logger)
    {
        _applications = applications;
        _config = config;
        _store = store;
        _dataLogger = dataLogger;
        _notifications = notifications;
        _trigger = trigger;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        var nextPrograms = DateTime.MinValue;
        var nextUsers = DateTime.MinValue;
        var nextServices = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            var general = _config.GetGeneral();
            var now = DateTime.UtcNow;
            try
            {
                if (now >= nextPrograms)
                {
                    nextPrograms = now.AddSeconds(general.Seconds(general.ProgramsIntervalSeconds, 5));
                    CheckPrograms(await _applications.GetInstalledProgramsAsync(stoppingToken));
                }

                if (now >= nextUsers)
                {
                    nextUsers = now.AddSeconds(general.Seconds(general.UsersIntervalSeconds, 5));
                    CheckUsers(await _applications.GetUsersAsync(stoppingToken));
                }

                if (now >= nextServices)
                {
                    nextServices = now.AddSeconds(general.Seconds(general.ServicesIntervalSeconds, 5));
                    CheckServices(await _applications.GetServicesAsync(stoppingToken));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "[Applications] Checking for changes failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private void CheckPrograms(IReadOnlyList<InstalledProgram> programs)
    {
        if (Baseline(ref _programs, programs, ApplicationChanges.ProgramsArea))
        {
            Report(ApplicationChanges.ProgramsArea, ApplicationChanges.ComparePrograms(_programs!, programs));
            _programs = programs;
        }
    }

    private void CheckUsers(IReadOnlyList<UserAccountDto> users)
    {
        if (Baseline(ref _users, users, ApplicationChanges.UsersArea))
        {
            Report(ApplicationChanges.UsersArea, ApplicationChanges.CompareUsers(_users!, users));
            _users = users;
        }
    }

    private void CheckServices(IReadOnlyList<SystemServiceDto> services)
    {
        if (Baseline(ref _services, services, ApplicationChanges.ServicesArea))
        {
            var (changes, settled) = ApplicationChanges.CompareServices(_services!, services);
            Report(ApplicationChanges.ServicesArea, changes);
            RecordServiceProblems(changes, services);
            _services = settled;
        }
    }

    /// <returns>True when there is an earlier reading to compare with. An empty reading after a full one is a failed read, not
    /// everything being removed, so it is skipped.</returns>
    private bool Baseline<T>(ref IReadOnlyList<T>? previous, IReadOnlyList<T> current, string area)
    {
        if (previous is null)
        {
            previous = current;
            _logger.LogInformation("[Applications] Watching {Count} {Area} for changes", current.Count, area.ToLowerInvariant());
            return false;
        }

        return current.Count > 0 || previous.Count == 0;
    }

    private void Report(string area, List<ApplicationChange> changes)
    {
        if (changes.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var change in changes)
        {
            _logger.LogInformation("[Applications] {Area} changed - {Severity}: {Change}", area, change.Severity.ToUpperInvariant(), change.Text);
        }

        _store.AddApplicationChanges(changes.Select(c => new ApplicationChangeRow(now, area, c.Severity, c.Text)));
        _dataLogger.Record($"{area} changed", [new AgentIssueDto
        {
            Id = $"apps:{area.ToLowerInvariant()}:changed",
            Severity = "Changed",
            Title = $"{area} changed ({changes.Count})",
            Message = string.Join("; ", changes.Select(c => c.Text)),
            TimestampUtc = now
        }]);

        var worst = changes.Any(c => c.Severity == "Critical") ? "Critical"
            : changes.Any(c => c.Severity == "Warning") ? "Warning"
            : "Success";
        var message = string.Join("; ", changes.Take(ShownInNotification).Select(c => c.Text))
            + (changes.Count > ShownInNotification ? $"; and {changes.Count - ShownInNotification} more." : ".");
        _notifications.ShowTemporary(new AgentIssueDto
        {
            Id = $"apps:{area.ToLowerInvariant()}:changed:{now.Ticks}",
            Severity = worst,
            Title = changes.Count == 1 ? $"{area}: 1 change" : $"{area}: {changes.Count} changes",
            Message = message,
            TimestampUtc = now
        }, NotificationDuration);
        _trigger.Request();
    }

    /// <summary>Opens a problem for each service that broke and closes it when the service recovers or is removed.</summary>
    private void RecordServiceProblems(List<ApplicationChange> changes, IReadOnlyList<SystemServiceDto> services)
    {
        var now = DateTime.UtcNow;
        var broken = new List<AgentIssueDto>();
        var recovered = new List<AgentIssueDto>();
        foreach (var change in changes.Where(c => c.ServiceName is not null))
        {
            var issue = new AgentIssueDto
            {
                Id = ApplicationChanges.ServiceIssuePrefix + change.ServiceName,
                Severity = change.Severity,
                Title = change.Text,
                Message = services.FirstOrDefault(s => s.Name == change.ServiceName)?.Problem ?? change.Text,
                TimestampUtc = now
            };
            (change.Recovered ? recovered : broken).Add(issue);
        }

        _dataLogger.Record(ServicesSource, broken);
        _dataLogger.RecordResolved(ServicesSource, recovered);
    }
}
