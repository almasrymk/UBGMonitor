using System.Globalization;
using MonitorAgent.Service.Config;
using MonitorAgent.Service.Monitoring;
using MonitorAgent.Service.Platform;

namespace MonitorAgent.Service.Reports;

/// <summary>
/// Deletes everything older than the "Keep data for" setting: the log files, the saved issue data, the
/// daily / weekly / monthly summaries and the rows of the reports database. Runs at start, every six hours
/// and right after the setting is changed.
/// </summary>
public sealed class DataCleaner : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan SettingPoll = TimeSpan.FromSeconds(30);

    private readonly ReportStore _store;
    private readonly ILocalConfigCache _config;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DataCleaner> _logger;

    public DataCleaner(ReportStore store, ILocalConfigCache config, IConfiguration configuration, ILogger<DataCleaner> logger)
    {
        _store = store;
        _config = config;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        var days = 0;
        var nextRunUtc = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            var current = _config.GetGeneral().RetentionDays;
            if (current != days || DateTime.UtcNow >= nextRunUtc)
            {
                var shrunk = days != 0 && current < days;
                days = current;
                nextRunUtc = DateTime.UtcNow + Interval;
                try
                {
                    await Task.Run(() => Clean(days, compact: shrunk), stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "[Cleanup] Removing old data failed");
                }
            }

            await Task.Delay(SettingPoll, stoppingToken);
        }
    }

    public void Clean(int days, bool compact)
    {
        var cutoffUtc = DateTime.UtcNow.AddDays(-days);
        var cutoffLocal = DateTime.Now.AddDays(-days);
        var rows = _store.RemoveOlderThan(cutoffUtc);
        if (compact && rows > 0)
        {
            _store.Compact();
        }

        var files = 0;
        files += DeleteFiles(LogFolder(), "*.log", cutoffLocal);
        files += DeleteFiles(MetricsRecorder.ReportsFolder, "*.html", cutoffLocal);
        var folders = DeleteDayFolders(IssueDataLogger.DataFolder, DateOnly.FromDateTime(cutoffLocal));
        _logger.LogInformation(
            "[Cleanup] Keeping {Days} days: removed {Rows} database row(s), {Files} file(s) and {Folders} day folder(s) older than {Cutoff:yyyy-MM-dd}",
            days, rows, files, folders, cutoffLocal);
    }

    /// <summary>
    /// The rolling log file path ("agent-.log" becomes agent-yyyyMMdd.log). A configured path that is not a full path
    /// on this system (such as the Windows path in the shipped settings on Linux) falls back to the system log folder.
    /// </summary>
    public static string LogPath(IConfiguration configuration)
    {
        var configured = configuration["LogPath"] ?? configuration["Serilog:WriteTo:0:Args:path"];
        return !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)
            ? configured
            : AgentPaths.DefaultLogPath;
    }

    private string LogFolder() =>
        Path.GetDirectoryName(Path.GetFullPath(LogPath(_configuration))) ?? AgentPaths.LogFolder;

    private int DeleteFiles(string folder, string pattern, DateTime cutoffLocal)
    {
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(folder, pattern))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoffLocal)
                {
                    File.Delete(file);
                    count++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug("[Cleanup] Could not delete {File}: {Message}", file, ex.Message);
            }
        }

        return count;
    }

    /// <summary>The saved issue data is in one folder per day named yyyy-MM-dd.</summary>
    private int DeleteDayFolders(string folder, DateOnly cutoff)
    {
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        var count = 0;
        foreach (var dayFolder in Directory.EnumerateDirectories(folder))
        {
            if (!DateOnly.TryParseExact(Path.GetFileName(dayFolder), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                || day >= cutoff)
            {
                continue;
            }

            try
            {
                Directory.Delete(dayFolder, recursive: true);
                count++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug("[Cleanup] Could not delete {Folder}: {Message}", dayFolder, ex.Message);
            }
        }

        return count;
    }
}
