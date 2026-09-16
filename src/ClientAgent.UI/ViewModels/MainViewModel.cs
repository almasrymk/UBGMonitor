using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClientAgent.Shared.Models;
using ClientAgent.UI.Enums;
using ClientAgent.UI.Services;

namespace ClientAgent.UI.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AgentApiClient _client;
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private string _windowTitle = "Client Agent Monitor — AGT-001";
    [ObservableProperty] private string _selectedTab = "Dashboard";
    [ObservableProperty] private string _agentStatus = "Connecting...";
    [ObservableProperty] private bool _serviceRunning;
    [ObservableProperty] private bool _madkhalConnected;
    [ObservableProperty] private bool _centralConnected;
    [ObservableProperty] private string _madkhalStatus = "Unknown";
    [ObservableProperty] private string _centralStatus = "Unknown";
    [ObservableProperty] private string _uptime = "-";
    [ObservableProperty] private long _pendingCount;
    [ObservableProperty] private string _pendingText = "0 events";
    [ObservableProperty] private string _sentTodayText = "0";
    [ObservableProperty] private string _agentVersion = "Agent v1.0.0";
    [ObservableProperty] private string _configVersionText = "Config v1";
    [ObservableProperty] private string _lastSyncText = "Last Sync: never";
    [ObservableProperty] private string _lastUpdate = "Never";
    [ObservableProperty] private string _refreshIntervalText = "3s";
    [ObservableProperty] private string _connectionMessage = string.Empty;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private int _criticalCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _healthyCount;
    [ObservableProperty] private int _unknownCount;

    public CpuViewModel Cpu { get; } = new();
    public RamViewModel Ram { get; } = new();
    public DiskViewModel Disk { get; } = new();
    public NetworkViewModel Network { get; } = new();
    public HardwareOsViewModel HardwareOs { get; }
    public ProcessListCardViewModel TopCpuCard { get; }
    public ProcessListCardViewModel TopRamCard { get; }
    public ProcessListCardViewModel TopNetworkCard { get; }
    public ObservableCollection<ProcessInfo> TopCpuProcesses { get; } = [];
    public ObservableCollection<ProcessInfo> TopRamProcesses { get; } = [];
    public ObservableCollection<MonitorPoint> MonitorPoints { get; } = [];
    public ObservableCollection<MonitoringEvent> RecentEvents { get; } = [];

    public MainViewModel() : this(new AgentApiClient())
    {
    }

    public MainViewModel(AgentApiClient client)
    {
        _client = client;
        TopCpuCard = new ProcessListCardViewModel(_client, "Top 5 by CPU", ProcessSortBy.Cpu, ResourceBrush("AccentGreenBrush", Color.FromRgb(0x4C, 0xAF, 0x50)));
        TopRamCard = new ProcessListCardViewModel(_client, "Top 5 by RAM", ProcessSortBy.Ram, ResourceBrush("ProcessBarRamBrush", Color.FromRgb(0x21, 0x96, 0xF3)));
        TopNetworkCard = new ProcessListCardViewModel(_client, "Top 5 by Network", ProcessSortBy.Network, Brushes.White);
        HardwareOs = new HardwareOsViewModel(_client);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    [RelayCommand]
    private void SelectTab(string tab)
    {
        SelectedTab = tab;
        if (tab == "Config")
        {
            // Settings button also lands here.
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsPaused)
        {
            return;
        }

        try
        {
            var status = await _client.GetStatusAsync();
            if (status is null)
            {
                MarkNotResponding();
                return;
            }

            WindowTitle = $"Client Agent Monitor — {(string.IsNullOrWhiteSpace(status.AgentId) ? "AGT-001" : status.AgentId)}";
            AgentStatus = status.Status;
            ServiceRunning = string.Equals(status.Status, "Running", StringComparison.OrdinalIgnoreCase);
            MadkhalConnected = status.MadkhalConnected;
            CentralConnected = status.CentralConnected;
            MadkhalStatus = status.MadkhalConnected ? "Connected" : "Offline";
            CentralStatus = status.CentralConnected ? "Connected" : "Offline";
            Uptime = FormatUptime(status.Uptime);
            PendingCount = status.PendingCount;
            PendingText = $"{status.PendingCount:N0} events";
            SentTodayText = $"{status.SentToday:N0}";
            AgentVersion = $"Agent v{status.Version}";
            ConfigVersionText = $"Config v{status.ConfigVersion}";
            LastSyncText = $"Last Sync: {FormatSync(status.LastSyncUtc)}";
            ConnectionMessage = string.Empty;

            var snapshot = await _client.GetSnapshotAsync();
            if (snapshot is null)
            {
                await LoadMetricsFallbackAsync();
            }
            else
            {
                ApplySnapshot(snapshot);
            }

            var points = await _client.GetMonitorPointsAsync() ?? [];
            var events = await _client.GetRecentEventsAsync(100) ?? [];
            Replace(MonitorPoints, points);
            Replace(RecentEvents, events);
            UpdatePointSummary(points, events);
            LastUpdate = DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception)
        {
            MarkNotResponding();
        }
    }

    private void ApplySnapshot(SystemSnapshot snapshot)
    {
        Cpu.Update(snapshot.Cpu);
        Ram.Update(snapshot.Ram);
        Disk.Update(snapshot.Partitions, snapshot.PhysicalDisks);
        Network.Update(snapshot.Network);
        Replace(TopRamProcesses, snapshot.TopProcesses.Take(5));
        Replace(TopCpuProcesses, snapshot.TopProcesses.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.RamMB).Take(5));
    }

    private async Task LoadMetricsFallbackAsync()
    {
        var cpu = await _client.GetCpuAsync();
        var ram = await _client.GetRamAsync();
        var partitions = await _client.GetPartitionsAsync();
        var network = await _client.GetNetworkAsync();
        var processes = await _client.GetTopProcessesAsync(10);

        if (cpu is not null) Cpu.Update(cpu);
        if (ram is not null) Ram.Update(ram);
        if (partitions is not null) Disk.Update(partitions, []);
        if (network is not null) Network.Update(network);
        if (processes is not null)
        {
            Replace(TopRamProcesses, processes.Take(5));
            Replace(TopCpuProcesses, processes.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.RamMB).Take(5));
        }
    }

    private void MarkNotResponding()
    {
        AgentStatus = "Service Not Responding";
        ServiceRunning = false;
        ConnectionMessage = "Service Not Responding";
    }

    [RelayCommand]
    private void Pause()
    {
        IsPaused = !IsPaused;
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        try
        {
            var snapshot = await _client.GetSnapshotAsync();
            if (snapshot is null)
            {
                return;
            }

            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"ubg-monitor-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            ConnectionMessage = "Export failed";
        }
    }

    [RelayCommand]
    private void OpenSettings() => SelectedTab = "Config";

    [RelayCommand]
    private void ViewAllMonitorPoints() => SelectedTab = "Monitor Points";

    private void UpdatePointSummary(IReadOnlyList<MonitorPoint> points, IReadOnlyList<MonitoringEvent> events)
    {
        var latest = events
            .GroupBy(e => e.MonitorPointId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.TimestampUtc).First().Severity, StringComparer.OrdinalIgnoreCase);

        CriticalCount = points.Count(p => latest.TryGetValue(p.MonitorPointId, out var s) && s == Severity.Critical);
        WarningCount = points.Count(p => latest.TryGetValue(p.MonitorPointId, out var s) && s == Severity.Warning);
        HealthyCount = points.Count(p => !latest.TryGetValue(p.MonitorPointId, out var s) || s == Severity.Info);
        UnknownCount = Math.Max(0, points.Count - CriticalCount - WarningCount - HealthyCount);
        if (points.Count == 0)
        {
            HealthyCount = 0;
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private static string FormatUptime(TimeSpan value)
        => value.TotalDays >= 1
            ? $"{(int)value.TotalDays}d {value.Hours}h {value.Minutes}m"
            : $"{value.Hours}h {value.Minutes}m {value.Seconds}s";

    private static string FormatSync(DateTime? utc)
    {
        if (!utc.HasValue)
        {
            return "never";
        }

        var elapsed = DateTime.UtcNow - utc.Value;
        if (elapsed.TotalMinutes < 1)
        {
            return "just now";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"{(int)elapsed.TotalMinutes}m ago";
        }

        return utc.Value.ToLocalTime().ToString("HH:mm");
    }

    public void Dispose()
    {
        _timer.Stop();
        HardwareOs.Dispose();
        TopCpuCard.Dispose();
        TopRamCard.Dispose();
        TopNetworkCard.Dispose();
    }

    private static Brush ResourceBrush(string key, Color fallback)
    {
        if (Application.Current?.TryFindResource(key) is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }
}
