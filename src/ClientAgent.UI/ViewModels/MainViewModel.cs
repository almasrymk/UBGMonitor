using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
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
    private readonly AppSettingsStore _appSettings = new();
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private string _windowTitle = "Client Agent Monitor — AGT-001";
    [ObservableProperty] private string _selectedTab = "Dashboard";
    [ObservableProperty] private string _agentStatus = "Connecting...";
    [ObservableProperty] private bool _serviceRunning;
    [ObservableProperty] private bool _internetConnected;
    [ObservableProperty] private string _internetStatus = "Internet: Disconnected";
    [ObservableProperty] private bool _madkhalConnected;
    [ObservableProperty] private bool _centralConnected;
    [ObservableProperty] private string _madkhalStatus = "Unknown";
    [ObservableProperty] private string _centralStatus = "Unknown";
    [ObservableProperty] private string _uptime = "-";
    [ObservableProperty] private string _cpuModelText = "CPU: -";
    [ObservableProperty] private string _ramTotalText = "RAM: -";
    [ObservableProperty] private string _osVersionText = "OS: -";
    [ObservableProperty] private string _agentVersion = "Agent v1.0.0";
    [ObservableProperty] private string _configVersionText = "Config v1";
    [ObservableProperty] private string _lastSyncText = "Last Sync: never";
    [ObservableProperty] private int _criticalCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _healthyCount;
    [ObservableProperty] private int _unknownCount;
    [ObservableProperty] private bool _hasCurrentIssues;
    [ObservableProperty] private bool _hasMonitorPoints;
    [ObservableProperty] private string? _selectedMonitorPointId;

    public SettingsViewModel Settings { get; } = new();

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
    public ObservableCollection<MonitorPointStatusDto> MonitorPoints { get; } = [];
    public ObservableCollection<DashboardMonitorPointViewModel> DashboardMonitorPoints { get; } = [];
    public ObservableCollection<AgentIssueDto> CurrentIssues { get; } = [];

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
        _timer = new DispatcherTimer();
        Settings.Saved += async (_, _) =>
        {
            ApplySavedRuntimeSettings();
            await RefreshAsync();
        };
        Settings.ApplicationsLoaded += async (_, _) => await RefreshAsync();
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.Theme))
            {
                UiTheme.Apply(Settings.Theme);
            }

            if (e.PropertyName == nameof(SettingsViewModel.NotificationsEnabled))
            {
                OnPropertyChanged(nameof(ShowNotifications));
            }
        };
        ApplySavedRuntimeSettings();
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    public bool ShowNotifications => Settings.NotificationsEnabled && HasCurrentIssues;

    private void ApplySavedRuntimeSettings()
    {
        _client.SetBaseAddress(Settings.ApiBaseUrl);
        var seconds = Settings.RefreshInterval < 1 ? 1 : Settings.RefreshInterval;
        _timer.Interval = TimeSpan.FromSeconds(seconds);
        UiTheme.Apply(Settings.Theme);
        OnPropertyChanged(nameof(ShowNotifications));
    }

    partial void OnHasCurrentIssuesChanged(bool value) => OnPropertyChanged(nameof(ShowNotifications));

    [RelayCommand]
    private void SelectTab(string tab)
    {
        SelectedTab = tab;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
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
            AgentVersion = $"Agent v{status.Version}";
            ConfigVersionText = $"Config v{status.ConfigVersion}";
            LastSyncText = $"Last Sync: {FormatSync(status.LastSyncUtc)}";

            var snapshot = await _client.GetSnapshotAsync();
            if (snapshot is null)
            {
                await LoadMetricsFallbackAsync();
            }
            else
            {
                ApplySnapshot(snapshot);
            }

            var points = MergeMonitorPoints(_appSettings.LoadMonitorPoints(), await _client.GetMonitorPointsAsync() ?? []);
            var shortcutPoints = OrderDashboardPoints(points).ToList();
            HasMonitorPoints = shortcutPoints.Count > 0;
            Replace(MonitorPoints, points);
            Replace(DashboardMonitorPoints, shortcutPoints.Select(DashboardMonitorPointViewModel.From));
            ApplyMonitorPointSelection(SelectedMonitorPointId);
            UpdatePointSummary(points);

            var issues = await _client.GetIssuesAsync();
            if (issues is not null)
            {
                Replace(CurrentIssues, issues);
                HasCurrentIssues = issues.Count > 0;
            }
        }
        catch (Exception)
        {
            MarkNotResponding();
        }
    }

    partial void OnInternetConnectedChanged(bool value)
        => InternetStatus = value ? "Internet: Connected" : "Internet: Disconnected";

    private void ApplyInternet(NetworkInfo? network)
    {
        var known = InterpretInternet(network);
        if (known is bool connected)
        {
            InternetConnected = connected;
            return;
        }

        _ = ProbeInternetAsync();
    }

    private static bool? InterpretInternet(NetworkInfo? network)
    {
        if (network is null)
        {
            return null;
        }

        if (!string.Equals(network.Status, "Up", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (network.PacketLossPercent >= 100)
        {
            return false;
        }

        return network.PingMs is null ? null : true;
    }

    private async Task ProbeInternetAsync()
    {
        var connected = await Task.Run(ProbeInternet);
        InternetConnected = connected;
    }

    private static bool ProbeInternet()
    {
        try
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return false;
            }

            using var ping = new Ping();
            var reply = ping.Send("1.1.1.1", 800);
            return reply?.Status == IPStatus.Success;
        }
        catch (Exception ex) when (ex is PingException or InvalidOperationException)
        {
            return false;
        }
    }

    private void ApplySnapshot(SystemSnapshot snapshot)
    {
        Cpu.Update(snapshot.Cpu);
        Ram.Update(snapshot.Ram);
        Disk.Update(snapshot.Partitions, snapshot.PhysicalDisks);
        Network.Update(snapshot.Network);
        ApplyInternet(snapshot.Network);
        UpdateHardwareStatus(snapshot.Cpu, snapshot.Ram, snapshot.Os);
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
        ApplyInternet(network);
        UpdateHardwareStatus(cpu, ram, await _client.GetOsAsync());
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
        _ = ProbeInternetAsync();
    }

    [RelayCommand]
    private void SelectMonitorPoint(DashboardMonitorPointViewModel? point)
    {
        if (point is null)
        {
            return;
        }

        ApplyMonitorPointSelection(point.MonitorPointId);
    }

    private void ApplyMonitorPointSelection(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || DashboardMonitorPoints.All(p => p.MonitorPointId != id))
        {
            id = DashboardMonitorPoints.FirstOrDefault()?.MonitorPointId;
        }

        SelectedMonitorPointId = id;
        foreach (var item in DashboardMonitorPoints)
        {
            item.IsSelected = item.MonitorPointId == id;
        }
    }

    [RelayCommand]
    private void ViewAllMonitorPoints() => SelectedTab = "Monitor Points";

    private void UpdateHardwareStatus(CpuInfo? cpu, RamInfo? ram, OsInfo? os)
    {
        CpuModelText = $"CPU: {(string.IsNullOrWhiteSpace(cpu?.Model) ? "-" : cpu.Model)}";
        RamTotalText = ram is null ? "RAM: -" : $"RAM: {ram.TotalGB:0.0} GB";
        var osName = os?.Name;
        var osVersion = os?.Version;
        OsVersionText = string.IsNullOrWhiteSpace(osName) && string.IsNullOrWhiteSpace(osVersion)
            ? "OS: -"
            : $"OS: {osName} {osVersion}".Trim();
    }

    private static string? ResolveIcon(MonitorPoint point)
    {
        if (point.Type == MonitorPointType.Website)
        {
            return point.Icon;
        }

        if (point.Type != MonitorPointType.Application)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(point.Icon)
            ? InstalledProgramCatalog.Find(point.Address)?.IconBase64
            : point.Icon;
    }

    private static List<MonitorPointStatusDto> MergeMonitorPoints(
        IReadOnlyList<MonitorPoint> configured,
        IReadOnlyList<MonitorPointStatusDto> live)
    {
        if (configured.Count == 0)
        {
            return [];
        }

        var liveById = live.ToDictionary(point => point.MonitorPointId, StringComparer.OrdinalIgnoreCase);
        return configured.Select(point =>
        {
            liveById.TryGetValue(point.MonitorPointId, out var status);
            var enabled = point.Enabled;
            return new MonitorPointStatusDto
            {
                MonitorPointId = point.MonitorPointId,
                DisplayName = string.IsNullOrWhiteSpace(point.DisplayName) ? point.MonitorPointId : point.DisplayName,
                Type = point.Type,
                DeviceKind = point.Type == MonitorPointType.Device ? point.DeviceKind : null,
                Glyph = DeviceIcons.Glyph(point.Type, point.Type == MonitorPointType.Device ? point.DeviceKind : null, point.DisplayName),
                Address = point.Address,
                Icon = ResolveIcon(point),
                Location = point.Location,
                Model = point.Model,
                Enabled = enabled,
                ShowInShortcut = point.ShowInShortcut,
                IntervalSeconds = point.IntervalSeconds,
                IsUp = status?.IsUp,
                Status = !enabled ? "Unknown" : status?.Status ?? "Unknown",
                LastCheckedUtc = status?.LastCheckedUtc,
                Message = status?.Message
            };
        }).ToList();
    }

    private static IEnumerable<MonitorPointStatusDto> OrderDashboardPoints(IEnumerable<MonitorPointStatusDto> points)
        => points
            .Where(ShowOnDashboard)
            .OrderBy(DashboardRank)
            .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase);

    private static bool ShowOnDashboard(MonitorPointStatusDto point)
        => point.ShowInShortcut || IsProblem(point.Status) || IsWarning(point.Status);

    private static int DashboardRank(MonitorPointStatusDto point)
    {
        if (IsProblem(point.Status))
        {
            return 0;
        }

        if (IsWarning(point.Status))
        {
            return 1;
        }

        return 2;
    }

    private static bool IsProblem(string status)
        => ContainsStatus(status, "Critical", "Offline", "Down");

    private static bool IsWarning(string status)
        => ContainsStatus(status, "Warning");

    private static bool ContainsStatus(string status, params string[] tokens)
        => tokens.Any(token => status.Contains(token, StringComparison.OrdinalIgnoreCase));

    private void UpdatePointSummary(IReadOnlyList<MonitorPointStatusDto> points)
    {
        CriticalCount = points.Count(p => string.Equals(p.Status, "Critical", StringComparison.OrdinalIgnoreCase));
        WarningCount = points.Count(p => string.Equals(p.Status, "Warning", StringComparison.OrdinalIgnoreCase));
        HealthyCount = points.Count(p => string.Equals(p.Status, "Healthy", StringComparison.OrdinalIgnoreCase));
        UnknownCount = points.Count(p => string.Equals(p.Status, "Unknown", StringComparison.OrdinalIgnoreCase)
                                         || string.IsNullOrWhiteSpace(p.Status));
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
