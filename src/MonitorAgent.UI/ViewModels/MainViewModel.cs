using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Monitoring;
using MonitorAgent.UI.Enums;
using MonitorAgent.UI.Services;

namespace MonitorAgent.UI.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AgentApiClient _client;
    private readonly UiTimer _timer;
    private int _refreshing;
    private bool _serviceConnected;
    private DateTime _nextServiceUtc = DateTime.MinValue;
    private DateTime _nextInternetUtc = DateTime.MinValue;
    private DateTime _nextCpuUtc = DateTime.MinValue;
    private DateTime _nextRamUtc = DateTime.MinValue;
    private DateTime _nextNetworkUtc = DateTime.MinValue;
    private DateTime _nextDiskUtc = DateTime.MinValue;
    private DateTime _nextHardwareUtc = DateTime.MinValue;
    private DateTime _nextLicenseUtc = DateTime.MinValue;
    private string _operatingSystem = string.Empty;
    private double _peakDownloadMbps;
    private double _peakUploadMbps;

    [ObservableProperty] private string _windowTitle = "MonitorAgent";
    [ObservableProperty] private string _selectedTab = "Dashboard";
    [ObservableProperty] private string _agentStatus = "Connecting...";
    [ObservableProperty] private bool _serviceRunning;
    [ObservableProperty] private bool _internetConnected;
    [ObservableProperty] private string _internetStatus = "Internet: Checking...";
    [ObservableProperty] private bool _madkhalConnected;
    [ObservableProperty] private bool _centralConnected;
    [ObservableProperty] private string _madkhalStatus = "Unknown";
    [ObservableProperty] private string _centralStatus = "Unknown";
    [ObservableProperty] private string _uptime = "-";
    [ObservableProperty] private string _cpuModelText = "CPU: -";
    [ObservableProperty] private string _ramTotalText = "RAM: -";
    [ObservableProperty] private string _osVersionText = "OS: -";
    [ObservableProperty] private string _agentVersion = "Agent: -";
    [ObservableProperty] private string _configVersionText = "Config v1";
    [ObservableProperty] private string _lastSyncText = "Last Sync: never";
    [ObservableProperty] private int _criticalCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _healthyCount;
    [ObservableProperty] private int _unknownCount;
    [ObservableProperty] private bool _hasCurrentIssues;
    [ObservableProperty] private bool _cpuSpecOk;
    [ObservableProperty] private bool _cpuSpecBad;
    [ObservableProperty] private bool _ramSpecOk;
    [ObservableProperty] private bool _ramSpecBad;
    [ObservableProperty] private bool _diskSpecOk;
    [ObservableProperty] private bool _diskSpecBad;
    [ObservableProperty] private bool _downloadSpecOk;
    [ObservableProperty] private bool _downloadSpecBad;
    [ObservableProperty] private bool _showDownloadSpec;
    [ObservableProperty] private bool _uploadSpecOk;
    [ObservableProperty] private bool _uploadSpecBad;
    [ObservableProperty] private bool _showUploadSpec;
    [ObservableProperty] private bool _internetSpecOk;
    [ObservableProperty] private bool _internetSpecBad;
    [ObservableProperty] private bool _showInternetSpec;
    [ObservableProperty] private bool _osSpecOk;
    [ObservableProperty] private bool _osSpecBad;
    [ObservableProperty] private bool _hardwareOsSpecOk;
    [ObservableProperty] private bool _hardwareOsSpecBad;
    [ObservableProperty] private bool _hasMonitorPoints;
    [ObservableProperty] private string? _selectedMonitorPointId;

    public SettingsViewModel Settings { get; }

    public ReportsViewModel Reports { get; }

    public LicenseViewModel License { get; }

    public CpuViewModel Cpu { get; } = new();
    public RamViewModel Ram { get; } = new();
    public DiskViewModel Disk { get; } = new();
    public NetworkViewModel Network { get; } = new();
    public HardwareOsViewModel HardwareOs { get; }
    public ProcessListCardViewModel TopCpuCard { get; }
    public ProcessListCardViewModel TopRamCard { get; }
    public ProcessListCardViewModel TopNetworkCard { get; }
    public ProcessListCardViewModel TopDiskCard { get; }
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
        Settings = new SettingsViewModel(client);
        Reports = new ReportsViewModel(client) { ShowRequested = () => SelectedTab = ReportsTab };
        License = new LicenseViewModel(client);
        License.RequiredChanged += OnLicenseRequiredChanged;
        TopCpuCard = new ProcessListCardViewModel(_client, "Top 5 by CPU", ProcessSortBy.Cpu, UiTheme.Resource("AccentGreenBrush", "#4CAF50"));
        TopRamCard = new ProcessListCardViewModel(_client, "Top 5 by RAM", ProcessSortBy.Ram, UiTheme.Resource("ProcessBarRamBrush", "#2196F3"));
        TopNetworkCard = new ProcessListCardViewModel(_client, "Top 5 by Network", ProcessSortBy.Network, UiTheme.FromHex("#FFFFFF"));
        TopDiskCard = new ProcessListCardViewModel(_client, "Top 5 by Disk", ProcessSortBy.Disk, UiTheme.Resource("ProcessBarDiskBrush", "#AB47BC"));
        HardwareOs = new HardwareOsViewModel(_client);
        _timer = new UiTimer(TimeSpan.FromSeconds(1));
        Settings.Saved += async (_, _) =>
        {
            ApplySavedRuntimeSettings();
            await RefreshAsync();
        };
        Settings.ApplicationsLoaded += async (_, _) => await RefreshAsync();
        Settings.ConnectRequested += async (_, _) =>
        {
            _serviceConnected = true;
            MarkNotResponding();
            await RefreshDueAsync();
        };
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.Theme))
            {
                UiTheme.Apply(Settings.Theme);
            }

            if (e.PropertyName == nameof(SettingsViewModel.NotificationsEnabled))
            {
                OnPropertyChanged(nameof(ShowNotifications));
                ApplyDeviceSpec();
            }

            if (e.PropertyName is nameof(SettingsViewModel.CpuMinCores)
                or nameof(SettingsViewModel.CpuWarningPercent)
                or nameof(SettingsViewModel.CpuProblemPercent)
                or nameof(SettingsViewModel.RamMinGb)
                or nameof(SettingsViewModel.RamWarningPercent)
                or nameof(SettingsViewModel.RamProblemPercent)
                or nameof(SettingsViewModel.DiskUnit)
                or nameof(SettingsViewModel.DiskMinimum)
                or nameof(SettingsViewModel.DiskTotalWarning)
                or nameof(SettingsViewModel.DiskPartitionWarning)
                or nameof(SettingsViewModel.DiskPartitionUnit)
                or nameof(SettingsViewModel.DiskRemainingWarning)
                or nameof(SettingsViewModel.DiskRemainingWarningUnit)
                or nameof(SettingsViewModel.DiskRemainingProblem)
                or nameof(SettingsViewModel.DiskRemainingProblemUnit)
                or nameof(SettingsViewModel.DownloadMinKbps)
                or nameof(SettingsViewModel.UploadMinKbps)
                or nameof(SettingsViewModel.InternetMinKbps)
                or nameof(SettingsViewModel.RequiredOperatingSystem)
                or nameof(SettingsViewModel.CpuAlert)
                or nameof(SettingsViewModel.RamAlert)
                or nameof(SettingsViewModel.InternetNotify)
                or nameof(SettingsViewModel.DownloadNotify)
                or nameof(SettingsViewModel.UploadNotify)
                or nameof(SettingsViewModel.DiskAlert)
                or nameof(SettingsViewModel.OsNotify))
            {
                ApplyDeviceSpec();
            }
        };
        Network.StartSpeedTest = () => _client.StartSpeedTestAsync();
        ApplySavedRuntimeSettings();
        _timer.Tick += async (_, _) => await RefreshDueAsync();
        _timer.Start();
        _ = RefreshDueAsync();
    }

    public bool ShowNotifications => Settings.NotificationsEnabled && HasCurrentIssues;

    /// <summary>The license screen covers the results; Settings and About stay usable.</summary>
    public bool ShowLicenseOverlay => License.IsRequired && SelectedTab is not (SettingsTab or AboutTab);

    private void OnLicenseRequiredChanged()
    {
        OnPropertyChanged(nameof(ShowLicenseOverlay));
        if (License.IsRequired)
        {
            return;
        }

        // The service kept measuring while there was no license, so everything is loaded again, history included.
        ApplySavedRuntimeSettings();
        if (SelectedTab == ReportsTab && !Reports.IsLoading)
        {
            Reports.GenerateCommand.Execute(null);
        }
    }

    private void ApplySavedRuntimeSettings()
    {
        _client.SetBaseAddress(Settings.ApiBaseUrl, Settings.ClientAccessKey);
        HardwareOs.SetHardwareInterval(Settings.HardwareOsIntervalSeconds);
        HardwareOs.SetNetworkInterval(Settings.NetworkIntervalSeconds);
        _nextServiceUtc = DateTime.MinValue;
        _nextInternetUtc = DateTime.MinValue;
        _nextCpuUtc = DateTime.MinValue;
        _nextRamUtc = DateTime.MinValue;
        _nextNetworkUtc = DateTime.MinValue;
        _nextDiskUtc = DateTime.MinValue;
        _nextHardwareUtc = DateTime.MinValue;
        UiTheme.Apply(Settings.Theme);
        OnPropertyChanged(nameof(ShowNotifications));
    }

    private async Task RefreshDueAsync()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        try
        {
            // Everything on screen comes from the service: when it stops answering, the app goes back to its empty state.
            var status = await _client.GetStatusAsync();
            if (status is null)
            {
                MarkNotResponding();
                return;
            }

            if (!_serviceConnected)
            {
                _serviceConnected = true;
                ApplySavedRuntimeSettings();
                if (SelectedTab == ReportsTab && !Reports.HasReport && !Reports.IsLoading)
                {
                    Reports.GenerateCommand.Execute(null);
                }
            }

            if (!Settings.IsLoaded && await Settings.LoadFromServiceAsync())
            {
                ApplySavedRuntimeSettings();
            }

            if (DateTime.UtcNow >= _nextLicenseUtc)
            {
                _nextLicenseUtc = DateTime.UtcNow.AddSeconds(License.IsRequired ? 5 : 15);
                await License.LoadAsync();
            }

            if (License.IsRequired)
            {
                ApplyServiceStatus(status);
                if (await _client.GetNotificationsAsync() is { } notice)
                {
                    ApplyNotifications(notice);
                }

                return;
            }

            if (await _client.GetDiskActivityAsync() is { } diskActivity)
            {
                Disk.UpdateActivity(diskActivity);
            }

            var now = DateTime.UtcNow;
            if (now >= _nextServiceUtc)
            {
                _nextServiceUtc = now.AddSeconds(AtLeastOne(Settings.RefreshInterval));
                await RefreshServiceAsync(status);
            }

            // Every tick, so a fixed problem or a settings change shows up as soon as the service knows it.
            if (await _client.GetNotificationsAsync() is { } notifications)
            {
                ApplyNotifications(notifications);
            }

            Network.SpeedTestEnabled = Settings.SpeedTestIntervalSeconds > 0;
            if (now >= _nextInternetUtc || Network.IsSpeedTestRunning)
            {
                _nextInternetUtc = now.AddSeconds(AtLeastOne(Settings.InternetIntervalSeconds));
                if (await _client.GetInternetAsync() is { } internet)
                {
                    if (internet.Connected is bool connected)
                    {
                        InternetConnected = connected;
                        InternetStatus = connected ? "Internet: Connected" : "Internet: Disconnected";
                        Network.SetInternetReachable(connected);
                    }

                    Network.ApplyInternetState(internet);
                }
            }

            if (now >= _nextCpuUtc)
            {
                _nextCpuUtc = now.AddSeconds(AtLeastOne(Settings.CpuIntervalSeconds));
                var cpu = await _client.GetCpuAsync();
                if (cpu is not null)
                {
                    Cpu.Update(cpu);
                    UpdateHardwareStatus(cpu, null, null);
                }
            }

            if (now >= _nextRamUtc)
            {
                _nextRamUtc = now.AddSeconds(AtLeastOne(Settings.RamIntervalSeconds));
                var ram = await _client.GetRamAsync();
                if (ram is not null)
                {
                    Ram.Update(ram);
                    UpdateHardwareStatus(null, ram, null);
                }
            }

            if (now >= _nextNetworkUtc)
            {
                _nextNetworkUtc = now.AddSeconds(AtLeastOne(Settings.NetworkIntervalSeconds));
                var network = await _client.GetNetworkAsync();
                if (network is not null)
                {
                    Network.Update(network);
                }
            }

            if (now >= _nextDiskUtc)
            {
                _nextDiskUtc = now.AddSeconds(AtLeastOne(Settings.DiskIntervalSeconds));
                var partitions = await _client.GetPartitionsAsync();
                var physical = await _client.GetPhysicalDisksAsync();
                if (partitions is not null)
                {
                    Disk.Update(partitions, physical ?? []);
                }
            }

            if (now >= _nextHardwareUtc)
            {
                _nextHardwareUtc = now.AddSeconds(AtLeastOne(Settings.HardwareOsIntervalSeconds));
                var os = await _client.GetOsAsync();
                UpdateHardwareStatus(null, null, os);
                await HardwareOs.RefreshStaticAsync();
            }

            ApplyDeviceSpec();
        }
        catch (Exception)
        {
            MarkNotResponding();
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private async Task RefreshServiceAsync(AgentStatusDto status)
    {
        ApplyServiceStatus(status);
        var points = MergeMonitorPoints(Settings.SavedMonitorPoints, await _client.GetMonitorPointsAsync() ?? []);
        var shortcutPoints = OrderDashboardPoints(points).ToList();
        HasMonitorPoints = shortcutPoints.Count > 0;
        Replace(MonitorPoints, points);
        Replace(DashboardMonitorPoints, shortcutPoints.Select(DashboardMonitorPointViewModel.From));
        ApplyMonitorPointSelection(SelectedMonitorPointId);
        UpdatePointSummary(points);
    }

    private void ApplyServiceStatus(AgentStatusDto status)
    {
        WindowTitle = $"MonitorAgent — {(string.IsNullOrWhiteSpace(Settings.MachineName) ? status.AgentId : Settings.MachineName)}";
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
    }

    private static int AtLeastOne(int value) => value < 1 ? 1 : value;

    partial void OnHasCurrentIssuesChanged(bool value) => OnPropertyChanged(nameof(ShowNotifications));

    private const string SettingsTab = "Settings";
    private const string ReportsTab = "Reports";
    private const string AboutTab = "About";

    [RelayCommand]
    private async Task SelectTab(string tab)
    {
        if (await CanLeaveCurrentTabAsync(tab))
        {
            SelectedTab = tab;
        }
    }

    private async Task<bool> CanLeaveCurrentTabAsync(string target)
    {
        if (SelectedTab != SettingsTab || target == SettingsTab)
        {
            return true;
        }

        if (!await Settings.TryLeaveSectionAsync())
        {
            // Staying on Settings: re-sync the menu, whose radio button already moved to the clicked tab.
            OnPropertyChanged(nameof(SelectedTab));
            return false;
        }

        return true;
    }

    private readonly Stack<string> _tabHistory = new();
    private bool _navigatingBack;

    public bool CanGoBack => _tabHistory.Count > 0;

    public string BackToolTip => CanGoBack ? $"Back to {_tabHistory.Peek()}" : "Back";

    partial void OnSelectedTabChanged(string? oldValue, string newValue)
    {
        if (!_navigatingBack && !string.IsNullOrEmpty(oldValue) && oldValue != newValue)
        {
            _tabHistory.Push(oldValue);
        }

        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(BackToolTip));
        OnPropertyChanged(nameof(ShowLicenseOverlay));
        GoBackCommand.NotifyCanExecuteChanged();

        if (newValue == ReportsTab && !Reports.HasReport && !Reports.IsLoading)
        {
            Reports.GenerateCommand.Execute(null);
        }
        else if (newValue == ReportsTab)
        {
            Reports.RefreshSubjects();
        }
        else if (newValue == AboutTab)
        {
            License.LoadCommand.Execute(null);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private async Task GoBack()
    {
        if (_tabHistory.Count == 0 || !await CanLeaveCurrentTabAsync(_tabHistory.Peek()))
        {
            return;
        }

        _navigatingBack = true;
        try
        {
            SelectedTab = _tabHistory.Pop();
        }
        finally
        {
            _navigatingBack = false;
        }

        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(BackToolTip));
        GoBackCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private Task RefreshAsync() => RefreshDueAsync();

    private void ApplySnapshot(SystemSnapshot snapshot)
    {
        Cpu.Update(snapshot.Cpu);
        Ram.Update(snapshot.Ram);
        Disk.Update(snapshot.Partitions, snapshot.PhysicalDisks);
        Network.Update(snapshot.Network);
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
        if (!_serviceConnected)
        {
            // Reports are asked for on their own, so one can arrive while the rest of the app already shows the service as gone.
            if (Reports.HasReport)
            {
                Reports.Unload();
            }

            return;
        }

        _serviceConnected = false;
        ClearServiceData();
    }

    /// <summary>Shows the app as it looks right after opening, before the service has sent anything.</summary>
    private void ClearServiceData()
    {
        WindowTitle = "MonitorAgent";
        MadkhalConnected = false;
        CentralConnected = false;
        MadkhalStatus = "Unknown";
        CentralStatus = "Unknown";
        Uptime = "-";
        CpuModelText = "CPU: -";
        RamTotalText = "RAM: -";
        OsVersionText = "OS: -";
        AgentVersion = "Agent: -";
        ConfigVersionText = "Config v1";
        LastSyncText = "Last Sync: never";
        InternetConnected = false;
        InternetStatus = "Internet: Checking...";
        _operatingSystem = string.Empty;
        _peakDownloadMbps = 0;
        _peakUploadMbps = 0;

        Cpu.Reset();
        Ram.Reset();
        Disk.Reset();
        Network.Reset();
        HardwareOs.Reset();
        TopCpuProcesses.Clear();
        TopRamProcesses.Clear();

        MonitorPoints.Clear();
        DashboardMonitorPoints.Clear();
        HasMonitorPoints = false;
        SelectedMonitorPointId = null;
        CriticalCount = 0;
        WarningCount = 0;
        HealthyCount = 0;
        UnknownCount = 0;
        CurrentIssues.Clear();
        HasCurrentIssues = false;
        ClearSpecMarks();
        Settings.Unload();
        Reports.Unload();
    }

    private void ClearSpecMarks()
    {
        CpuSpecOk = CpuSpecBad = false;
        RamSpecOk = RamSpecBad = false;
        DiskSpecOk = DiskSpecBad = false;
        DownloadSpecOk = DownloadSpecBad = ShowDownloadSpec = false;
        UploadSpecOk = UploadSpecBad = ShowUploadSpec = false;
        InternetSpecOk = InternetSpecBad = ShowInternetSpec = false;
        OsSpecOk = OsSpecBad = false;
        HardwareOsSpecOk = HardwareOsSpecBad = false;
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
    private Task ViewAllMonitorPoints() => SelectTab("Monitor Points");

    private void UpdateHardwareStatus(CpuInfo? cpu, RamInfo? ram, OsInfo? os)
    {
        if (cpu is not null)
        {
            CpuModelText = $"CPU: {(string.IsNullOrWhiteSpace(cpu.Model) ? "-" : cpu.Model)}";
        }

        if (ram is not null)
        {
            RamTotalText = $"RAM: {ram.TotalGB:0.0} GB";
        }

        if (os is not null)
        {
            var osName = os.Name;
            var osVersion = os.Version;
            _operatingSystem = $"{osName} {osVersion}".Trim();
            OsVersionText = string.IsNullOrWhiteSpace(osName) && string.IsNullOrWhiteSpace(osVersion)
                ? "OS: -"
                : $"OS: {_operatingSystem}";
        }
    }

    private void ApplyDeviceSpec()
    {
        if (!_serviceConnected)
        {
            ClearSpecMarks();
            return;
        }

        if (Network.DownloadMbps > _peakDownloadMbps)
        {
            _peakDownloadMbps = Network.DownloadMbps;
        }

        if (Network.UploadMbps > _peakUploadMbps)
        {
            _peakUploadMbps = Network.UploadMbps;
        }

        var partitions = Disk.Partitions
            .Select(row => new DiskSlice(row.Drive, row.TotalGb, row.FreeGb, row.UsagePercent))
            .ToList();
        var spec = Settings.CaptureDeviceSpec();
        foreach (var row in HardwareOs.Level1Rows.Concat(HardwareOs.Level4Rows))
        {
            row.SpecMet = DeviceSpecEvaluator.RowMeetsSpec(spec, row.Label, row.Value);
        }

        var result = DeviceSpecEvaluator.Evaluate(spec, new DeviceSpecReading(
            Cpu.LogicalCores,
            Cpu.UsagePercent,
            Ram.TotalGb,
            Ram.UsagePercent,
            partitions,
            InternetConnected,
            Network.TestedDownloadMbps ?? _peakDownloadMbps,
            Network.TestedUploadMbps ?? _peakUploadMbps,
            _operatingSystem));

        CpuSpecOk = result.CpuOk;
        CpuSpecBad = result.CpuBad;
        RamSpecOk = result.RamOk;
        RamSpecBad = result.RamBad;
        DiskSpecOk = result.DiskOk;
        DiskSpecBad = result.DiskBad;
        DownloadSpecOk = result.DownloadOk;
        DownloadSpecBad = result.DownloadBad;
        ShowDownloadSpec = result.DownloadOk || result.DownloadBad;
        UploadSpecOk = result.UploadOk;
        UploadSpecBad = result.UploadBad;
        ShowUploadSpec = result.UploadOk || result.UploadBad;
        InternetSpecOk = result.InternetOk;
        InternetSpecBad = result.InternetBad;
        ShowInternetSpec = result.InternetOk || result.InternetBad;
        OsSpecOk = result.OsOk;
        OsSpecBad = result.OsBad;

        var specRows = HardwareOs.Level1Rows.Concat(HardwareOs.Level4Rows).Select(row => row.SpecMet).ToList();
        HardwareOsSpecBad = OsSpecBad || specRows.Contains(false);
        HardwareOsSpecOk = !HardwareOsSpecBad && (OsSpecOk || specRows.Contains(true));

        if (!Settings.NotificationsEnabled)
        {
            ApplyNotifications([]);
        }
    }

    /// <summary>The notifications are built by the service; the app only shows them.</summary>
    private void ApplyNotifications(IReadOnlyList<AgentIssueDto> notifications)
    {
        SyncIssues(Settings.NotificationsEnabled ? notifications : []);
        HasCurrentIssues = CurrentIssues.Count > 0;
    }

    private void SyncIssues(IReadOnlyList<AgentIssueDto> issues)
    {
        for (var i = 0; i < issues.Count; i++)
        {
            var issue = issues[i];
            var existing = -1;
            for (var j = i; j < CurrentIssues.Count; j++)
            {
                if (CurrentIssues[j].Id == issue.Id)
                {
                    existing = j;
                    break;
                }
            }

            if (existing < 0)
            {
                CurrentIssues.Insert(i, issue);
                continue;
            }

            if (existing != i)
            {
                CurrentIssues.Move(existing, i);
            }

            var current = CurrentIssues[i];
            if (current.Severity != issue.Severity || current.Title != issue.Title || current.Message != issue.Message)
            {
                CurrentIssues[i] = issue;
            }
        }

        while (CurrentIssues.Count > issues.Count)
        {
            CurrentIssues.RemoveAt(CurrentIssues.Count - 1);
        }
    }

    private static string? ResolveIcon(MonitorPoint point)
    {
        if (point.Type == MonitorPointType.Website)
        {
            return point.Icon;
        }

        if (point.Type == MonitorPointType.Database)
        {
            return DatabaseLogos.Base64(point.Database?.Engine);
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
                Alert = point.Alert,
                IsUp = status?.IsUp,
                Status = !enabled ? "Unknown" : status?.Status ?? "Unknown",
                LastCheckedUtc = status?.LastCheckedUtc,
                Message = status?.Message,
                ResponseMs = enabled ? status?.ResponseMs : null,
                StatusSinceUtc = enabled ? status?.StatusSinceUtc : null,
                Target = MonitorPointText.Target(point)
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
        TopDiskCard.Dispose();
    }
}
