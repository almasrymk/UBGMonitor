using ClientAgent.Service.Config;
using ClientAgent.Service.Monitoring;
using ClientAgent.Service.SystemInfo;
using ClientAgent.Shared.Models.Reports;
using ClientAgent.Shared.Reports;

namespace ClientAgent.Service.Reports;

/// <summary>
/// Saves a reading every minute for the reports (CPU, RAM, temperature, disks, disk activity, network usage, ping,
/// Wi-Fi, internet, monitor points and their response times, and the busiest programs while CPU or RAM is high),
/// and writes the daily, weekly and monthly
/// summaries to the Reports folder next to the service.
/// </summary>
public sealed class MetricsRecorder : BackgroundService
{
    public static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(1);

    private const int BusyProgramsCount = 5;
    private const int DefaultBusyPercent = 80;

    private readonly IServiceProvider _services;
    private readonly ReportStore _store;
    private readonly ILocalConfigCache _config;
    private readonly ILogger<MetricsRecorder> _logger;
    private DateOnly _today = DateOnly.FromDateTime(DateTime.Now);
    private (long Received, long Sent)? _lastBytes;

    public MetricsRecorder(IServiceProvider services, ReportStore store, ILocalConfigCache config, ILogger<MetricsRecorder> logger)
    {
        _services = services;
        _store = store;
        _config = config;
        _logger = logger;
    }

    public static string ReportsFolder => Path.Combine(AppContext.BaseDirectory, "Reports");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        using var timer = new PeriodicTimer(SampleInterval);
        do
        {
            try
            {
                await SampleAsync(stoppingToken);
                await RollOverDayAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "[Reports] Saving the reading failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SampleAsync(CancellationToken cancellationToken)
    {
        var system = _services.GetRequiredService<ISystemInfoService>();
        var sensors = _services.GetRequiredService<ISensorsService>();
        var internet = _services.GetRequiredService<IInternetStatus>();

        var cpuTask = system.GetCpuAsync(cancellationToken);
        var ramTask = system.GetRamAsync(cancellationToken);
        var partitionsTask = system.GetPartitionsAsync(cancellationToken);
        var sensorsTask = sensors.GetSensorsAsync(cancellationToken);
        var pointsTask = MonitorPointStatusBuilder.BuildAsync(_services, cancellationToken);
        var networkTask = _services.GetRequiredService<INetworkService>().ReadForReportAsync(cancellationToken);
        await Task.WhenAll(cpuTask, ramTask, partitionsTask, sensorsTask, pointsTask, networkTask);

        var sensorValues = sensorsTask.Result;
        var temperature = new[] { sensorValues.CpuTempC, sensorValues.GpuTempC, sensorValues.MotherboardTempC }
            .Where(value => value is > 0).Select(value => value!.Value).DefaultIfEmpty(double.NaN).Max();

        bool? internetUp = null;
        bool? networkUp = null;
        if (internet.GetState().CheckedUtc is not null)
        {
            networkUp = internet.Problem != ConnectivityProblem.Network;
            internetUp = internet.Problem == ConnectivityProblem.None;
        }

        var network = networkTask.Result;
        double? receivedMb = null;
        double? sentMb = null;
        if (_lastBytes is { } last && network.ReceivedBytes >= last.Received && network.SentBytes >= last.Sent)
        {
            receivedMb = (network.ReceivedBytes - last.Received) / 1024d / 1024d;
            sentMb = (network.SentBytes - last.Sent) / 1024d / 1024d;
        }

        _lastBytes = network.ReceivedBytes > 0 || network.SentBytes > 0 ? (network.ReceivedBytes, network.SentBytes) : null;

        var disk = _services.GetRequiredService<IDiskActivityService>().TakeAverage();
        var cpu = cpuTask.Result.UsagePercent;
        var ram = ramTask.Result.UsagePercent;
        var processes = await BusyProgramsAsync(system, cpu, ram, cancellationToken);
        var points = pointsTask.Result.Where(point => point.Enabled).ToList();
        _store.AddSample(
            new SampleRow(
                DateTime.UtcNow, cpu, ram, double.IsNaN(temperature) ? null : temperature, internetUp, networkUp,
                network.PingMs, network.LossPercent, network.WifiSignalPercent, receivedMb, sentMb,
                disk?.ActiveTimePercent, disk is null ? null : disk.ReadBytesPerSecond / 1024 / 1024,
                disk is null ? null : disk.WriteBytesPerSecond / 1024 / 1024, disk?.ResponseMs),
            partitionsTask.Result.Select(p => (p.DriveLetter.TrimEnd('\\'), p.TotalGB, p.FreeGB)),
            points.Select(point => (point.MonitorPointId, point.DisplayName, point.Type.ToString(), point.Status ?? "Unknown", point.ResponseMs)),
            processes);
        _logger.LogInformation("[Reports] Reading saved: CPU {Cpu:0.#}%, RAM {Ram:0.#}%, ping {Ping}, {Disks} disk(s), {Points} monitor point(s){Busy}",
            cpu, ram, network.PingMs is { } ping ? $"{ping:0} ms" : "-", partitionsTask.Result.Count, points.Count,
            processes.Count > 0 ? $", {processes.Count} busy program(s)" : string.Empty);
    }

    /// <summary>The programs using the most CPU or RAM, saved only while usage is at or above the warning level.</summary>
    private async Task<List<(string Kind, string Name, double Value)>> BusyProgramsAsync(
        ISystemInfoService system, double cpu, double ram, CancellationToken cancellationToken)
    {
        var spec = _config.GetDeviceSpec();
        var list = new List<(string Kind, string Name, double Value)>();
        if (cpu >= (spec.CpuWarningPercent > 0 ? spec.CpuWarningPercent : DefaultBusyPercent))
        {
            var top = await system.GetTopProcessesSortedAsync(BusyProgramsCount, "cpu", cancellationToken);
            list.AddRange(top.Where(p => p.Value > 0).Select(p => ("cpu", p.Name, p.Value)));
        }

        if (ram >= (spec.RamWarningPercent > 0 ? spec.RamWarningPercent : DefaultBusyPercent))
        {
            var top = await system.GetTopProcessesSortedAsync(BusyProgramsCount, "ram", cancellationToken);
            list.AddRange(top.Where(p => p.Value > 0).Select(p => ("ram", p.Name, p.Value)));
        }

        return list;
    }

    private async Task RollOverDayAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today == _today)
        {
            return;
        }

        var yesterday = _today;
        _today = today;

        var end = today.ToDateTime(TimeOnly.MinValue);
        await SaveSummaryAsync(yesterday.ToDateTime(TimeOnly.MinValue), end, $"{yesterday:yyyy-MM-dd} Daily Summary");
        if (today.DayOfWeek == DayOfWeek.Saturday)
        {
            var start = end.AddDays(-7);
            await SaveSummaryAsync(start, end, $"{start:yyyy-MM-dd} to {end.AddDays(-1):yyyy-MM-dd} Weekly Summary");
        }

        if (today.Day == 1)
        {
            var start = end.AddMonths(-1);
            await SaveSummaryAsync(start, end, $"{start:yyyy-MM} Monthly Summary");
        }
    }

    private async Task SaveSummaryAsync(DateTime from, DateTime to, string name)
    {
        try
        {
            var report = await _services.GetRequiredService<ReportBuilder>().BuildAsync(ReportTypes.Summary, from, to, null, CancellationToken.None);
            Directory.CreateDirectory(ReportsFolder);
            var file = Path.Combine(ReportsFolder, $"{name}.html");
            await File.WriteAllTextAsync(file, ReportExporter.ToHtml(report));
            _logger.LogInformation("[Reports] {Name} saved to {File}", name, file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "[Reports] Writing the {Name} failed", name);
        }
    }
}
