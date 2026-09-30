using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClientAgent.Shared.Models;
using ClientAgent.UI.Models;
using ClientAgent.UI.Services;

namespace ClientAgent.UI.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    public const string SectionGeneral = "General";
    public const string SectionMonitorPoints = "Monitor Points";
    public const string SectionConditions = "Conditions";
    public const string AllMonitorPointsId = "all";

    private const int DefaultRefreshInterval = 3;
    private const int DefaultInternetInterval = 5;
    private const int DefaultSpeedTestInterval = 1800;
    private const int DefaultCpuInterval = 3;
    private const int DefaultRamInterval = 3;
    private const int DefaultNetworkInterval = 3;
    private const int DefaultDiskInterval = 15;
    private const int DefaultHardwareOsInterval = 30;
    private const string DefaultApiBaseUrl = "http://127.0.0.1:5050";
    private const string DefaultTheme = "Dark";
    private const bool DefaultNotificationsEnabled = true;
    private const int DefaultRetentionDays = 90;
    private const int DefaultServicePort = 5050;
    private const string LocalOnlyAddress = "127.0.0.1";
    private const string AllAddresses = "0.0.0.0";

    private readonly AppSettingsStore _store;
    private readonly AgentApiClient _client;
    private UiAppSettings _snapshot = new();
    private List<MonitorPoint> _monitorPointSnapshot = [];

    [ObservableProperty] private string _selectedSection = SectionGeneral;
    [ObservableProperty] private string _machineName = Environment.MachineName;
    [ObservableProperty] private int _refreshInterval = DefaultRefreshInterval;
    [ObservableProperty] private int _internetIntervalSeconds = DefaultInternetInterval;
    [ObservableProperty] private int _speedTestIntervalSeconds = DefaultSpeedTestInterval;
    [ObservableProperty] private int _cpuIntervalSeconds = DefaultCpuInterval;
    [ObservableProperty] private int _ramIntervalSeconds = DefaultRamInterval;
    [ObservableProperty] private int _networkIntervalSeconds = DefaultNetworkInterval;
    [ObservableProperty] private int _diskIntervalSeconds = DefaultDiskInterval;
    [ObservableProperty] private int _hardwareOsIntervalSeconds = DefaultHardwareOsInterval;
    [ObservableProperty] private string _apiBaseUrl = DefaultApiBaseUrl;
    [ObservableProperty] private string _clientAccessKey = string.Empty;
    [ObservableProperty] private string _connectionMessage = string.Empty;
    [ObservableProperty] private bool _isConnectionOk;
    [ObservableProperty] private string _theme = DefaultTheme;
    [ObservableProperty] private bool _notificationsEnabled = DefaultNotificationsEnabled;
    [ObservableProperty] private int _dataRetentionDays = DefaultRetentionDays;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsServiceOpenWithoutKey))]
    private string _serviceListenAddress = LocalOnlyAddress;
    [ObservableProperty] private int _servicePort = DefaultServicePort;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsServiceOpenWithoutKey))]
    private string _remoteAccessKey = string.Empty;
    [ObservableProperty] private int _cpuMinCores = 4;
    [ObservableProperty] private int _cpuWarningPercent = 85;
    [ObservableProperty] private int _cpuProblemPercent = 95;
    [ObservableProperty] private double _ramMinGb = 8;
    [ObservableProperty] private int _ramWarningPercent = 85;
    [ObservableProperty] private int _ramProblemPercent = 95;
    [ObservableProperty] private string _diskUnit = "Percent";
    [ObservableProperty] private double _diskMinimum = 128;
    [ObservableProperty] private double _diskTotalWarning = 90;
    [ObservableProperty] private double _diskPartitionWarning = 90;
    [ObservableProperty] private string _diskPartitionUnit = "Percent";
    [ObservableProperty] private double _downloadMinKbps = 10000;
    [ObservableProperty] private double _uploadMinKbps = 2000;
    [ObservableProperty] private double _internetMinKbps = 10000;
    [ObservableProperty] private double _diskRemainingWarning = 15;
    [ObservableProperty] private string _diskRemainingWarningUnit = "Percent";
    [ObservableProperty] private double _diskRemainingProblem = 5;
    [ObservableProperty] private string _diskRemainingProblemUnit = "Percent";
    [ObservableProperty] private string _requiredOperatingSystem = "Windows 10";
    [ObservableProperty] private MonitorPointAlert _cpuAlert = MonitorPointAlert.Problem;
    [ObservableProperty] private MonitorPointAlert _ramAlert = MonitorPointAlert.Problem;
    [ObservableProperty] private bool _internetNotify = true;
    [ObservableProperty] private bool _downloadNotify = true;
    [ObservableProperty] private bool _uploadNotify = true;
    [ObservableProperty] private MonitorPointAlert _diskAlert = MonitorPointAlert.Problem;
    [ObservableProperty] private bool _osNotify = true;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private MonitorPointSettingViewModel? _selectedMonitorPoint;
    [ObservableProperty] private ConditionSettingViewModel? _selectedCondition;

    public event EventHandler? Saved;

    public event EventHandler? ApplicationsLoaded;

    /// <summary>The app was pointed at another service (or the same one with another key): everything must reload from it.</summary>
    public event EventHandler? ConnectRequested;

    /// <summary>Asks the user to go on with a change that cannot be undone; the argument is the warning. True = go on.</summary>
    public Func<string, bool>? ConfirmWarning { get; set; }

    public SettingsViewModel(AgentApiClient client)
    {
        _client = client;
        _store = new AppSettingsStore(client);
        TargetOptions.Add(new TargetOption(AllMonitorPointsId, "All monitor points"));
        ShowUnloaded();
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusMessage = string.Empty;
        };
        _savedTimer.Tick += (_, _) =>
        {
            _savedTimer.Stop();
            IsSavedBadgeVisible = false;
        };

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(HasChanges) or nameof(StatusMessage) or nameof(SelectedSection)
                or nameof(SelectedMonitorPoint) or nameof(SelectedCondition) or nameof(HasSelectedCondition)
                or nameof(IsLightTheme) or nameof(IsSavedBadgeVisible) or nameof(SelectedSectionTitle)))
            {
                RefreshHasChanges();
            }
        };
        MonitorPoints.CollectionChanged += (_, _) => RefreshHasChanges();
        Conditions.CollectionChanged += (_, e) =>
        {
            foreach (var item in e.NewItems?.OfType<ConditionSettingViewModel>() ?? [])
            {
                item.PropertyChanged += OnConditionPropertyChanged;
            }

            foreach (var item in e.OldItems?.OfType<ConditionSettingViewModel>() ?? [])
            {
                item.PropertyChanged -= OnConditionPropertyChanged;
            }

            RefreshHasChanges();
        };
        foreach (var condition in Conditions)
        {
            condition.PropertyChanged += OnConditionPropertyChanged;
        }

        _ = LoadInstalledApplicationsAsync();
    }

    /// <summary>True once the settings came from the service; editing and saving need the service.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(DeviceLabel))]
    private bool _isLoaded;

    /// <summary>The device name from the service's settings, or "-" while the service is not answering.</summary>
    public string DeviceLabel => IsLoaded ? MachineName : "-";

    partial void OnMachineNameChanged(string value) => OnPropertyChanged(nameof(DeviceLabel));

    /// <summary>The connection to the service whose settings these are.</summary>
    public AgentApiClient Client => _client;

    /// <summary>The monitor points as last loaded from / saved to the service.</summary>
    public IReadOnlyList<MonitorPoint> SavedMonitorPoints => _monitorPointSnapshot;

    /// <summary>Loads the settings from the service; false when it did not answer.</summary>
    public async Task<bool> LoadFromServiceAsync()
    {
        if (IsLoaded)
        {
            return true;
        }

        if (await _store.LoadAsync() is not { } loaded)
        {
            return false;
        }

        ApplyAll(loaded.Settings, loaded.MonitorPoints);
        IsLoaded = true;
        SavePreferences();
        await LoadServiceAddressesAsync();
        return true;
    }

    private async Task LoadServiceAddressesAsync()
    {
        var addresses = (await _client.GetStatusAsync())?.Addresses ?? [];
        foreach (var address in addresses)
        {
            AddListenOption(address);
        }
    }

    /// <summary>Points the app at the service typed in "This app connects to" and reloads everything from it.</summary>
    [RelayCommand]
    private async Task Connect()
    {
        if (AgentApiClient.ParseAddress(ApiBaseUrl) is not { } uri)
        {
            IsConnectionOk = false;
            ConnectionMessage = "Type the service address, for example 192.168.1.10:5050 or http://pc-name:5050.";
            return;
        }

        ApiBaseUrl = uri.GetLeftPart(UriPartial.Authority);
        ClientAccessKey = ClientAccessKey.Trim();
        SavePreferences();
        _client.SetBaseAddress(ApiBaseUrl, ClientAccessKey);
        IsConnectionOk = false;
        ConnectionMessage = "Connecting...";
        var error = await _client.CheckConnectionAsync();
        IsConnectionOk = error is null;
        ConnectionMessage = error ?? $"Connected to {ApiBaseUrl}.";
        ConnectRequested?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<RetentionOption> RetentionOptions { get; } =
    [
        new(30, "1 month"),
        new(60, "2 months"),
        new(90, "3 months"),
        new(180, "6 months"),
        new(270, "9 months"),
        new(365, "1 year")
    ];

    /// <summary>127.0.0.1 = this computer only, 0.0.0.0 = all network cards, then the service computer's own IPs.</summary>
    public ObservableCollection<ListenOption> ListenAddressOptions { get; } =
    [
        new(LocalOnlyAddress, "127.0.0.1  (this computer only)"),
        new(AllAddresses, "0.0.0.0  (all network cards)")
    ];

    private void AddListenOption(string address)
    {
        if (ListenAddressOptions.All(option => option.Address != address))
        {
            ListenAddressOptions.Add(new ListenOption(address, $"{address}  (this network card only)"));
        }
    }

    /// <summary>The service is open to the network and any computer can use it.</summary>
    public bool IsServiceOpenWithoutKey =>
        ServiceListenAddress.Trim() != LocalOnlyAddress && string.IsNullOrWhiteSpace(RemoteAccessKey);

    private static int RetentionDays(int days) => days <= 0 ? DefaultRetentionDays : Math.Clamp(days, 30, 365);

    private static string RetentionLabel(int days) => days switch
    {
        365 => "1 year",
        _ when days % 30 == 0 => days / 30 == 1 ? "1 month" : $"{days / 30} months",
        _ => $"{days} days"
    };

    private static bool IsListenAddress(string address) =>
        System.Net.IPAddress.TryParse(address, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;

    /// <summary>Shows the warnings for the General changes being saved; false when the user said no to one.</summary>
    private bool ConfirmGeneralChanges(GeneralSettings current, GeneralSettings saved)
    {
        if (ConfirmWarning is null)
        {
            return true;
        }

        if (current.DataRetentionDays < saved.DataRetentionDays
            && !ConfirmWarning($"The data will now be kept for {RetentionLabel(current.DataRetentionDays)} instead of {RetentionLabel(saved.DataRetentionDays)}.\n\n"
                + $"Logs, saved data and report history older than {RetentionLabel(current.DataRetentionDays)} will be deleted permanently. "
                + "Deleted data cannot be restored.\n\nDo you want to continue?"))
        {
            return false;
        }

        if (current.DataRetentionDays > saved.DataRetentionDays
            && !ConfirmWarning($"The data will now be kept for {RetentionLabel(current.DataRetentionDays)}.\n\n"
                + $"From now on, logs, saved data and report history older than {RetentionLabel(current.DataRetentionDays)} are deleted permanently and cannot be restored. "
                + "Data that was already deleted does not come back.\n\nDo you want to continue?"))
        {
            return false;
        }

        var listenChanged = current.ServiceListenAddress != saved.ServiceListenAddress || current.ServicePort != saved.ServicePort;
        var remote = AgentApiClient.ParseAddress(ApiBaseUrl) is { IsLoopback: false };
        return !listenChanged || !remote
            || ConfirmWarning("This app is connected to the service from another computer. After the service moves to "
                + $"{current.ServiceListenAddress}:{current.ServicePort}, this app may lose the connection and need its address changed.\n\nDo you want to continue?");
    }

    /// <summary>After the service on this computer moves to another port, the app follows it.</summary>
    private void FollowServicePort(GeneralSettings current, GeneralSettings saved)
    {
        if (current.ServicePort == saved.ServicePort || AgentApiClient.ParseAddress(ApiBaseUrl) is not { IsLoopback: true } uri)
        {
            return;
        }

        ApiBaseUrl = new UriBuilder(uri) { Port = current.ServicePort }.Uri.GetLeftPart(UriPartial.Authority);
        SavePreferences();
    }

    /// <summary>The service stopped: back to empty defaults, keeping only the theme and the service address.</summary>
    public void Unload()
    {
        if (!IsLoaded)
        {
            return;
        }

        IsLoaded = false;
        ShowUnloaded();
    }

    private void ShowUnloaded()
    {
        var preferences = ClientPreferences.Load();
        var defaults = new UiAppSettings();
        defaults.General.Theme = preferences.Theme;
        ApiBaseUrl = string.IsNullOrWhiteSpace(preferences.ApiBaseUrl) ? DefaultApiBaseUrl : preferences.ApiBaseUrl.Trim();
        ClientAccessKey = preferences.AccessKey;
        while (ListenAddressOptions.Count > 2)
        {
            ListenAddressOptions.RemoveAt(ListenAddressOptions.Count - 1);
        }

        ApplyAll(defaults, []);
        StatusMessage = string.Empty;
    }

    private void ApplyAll(UiAppSettings settings, IReadOnlyList<MonitorPoint> points)
    {
        _suspendChangeTracking = true;
        try
        {
            Apply(settings);
            ReplaceMonitorPoints(points);
        }
        finally
        {
            _suspendChangeTracking = false;
        }

        _snapshot = Capture();
        _monitorPointSnapshot = CaptureMonitorPoints();
        MarkAllClean();
    }

    private void SavePreferences() =>
        new ClientPreferences
        {
            ApiBaseUrl = string.IsNullOrWhiteSpace(ApiBaseUrl) ? DefaultApiBaseUrl : ApiBaseUrl.Trim(),
            AccessKey = ClientAccessKey.Trim(),
            Theme = _snapshot.General.Theme
        }.Save();

    private bool CanSave() => HasChanges && IsLoaded;

    private static readonly string[] Sections = [SectionGeneral, SectionMonitorPoints, SectionConditions];
    private static readonly TimeSpan StatusMessageDuration = TimeSpan.FromSeconds(4);

    private readonly Dictionary<string, string> _savedFingerprints = new();
    private readonly System.Windows.Threading.DispatcherTimer _statusTimer = new() { Interval = StatusMessageDuration };
    private bool _suspendChangeTracking;

    /// <summary>True when the selected section differs from what was last loaded or saved.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _hasChanges;

    /// <summary>
    /// Asks what to do with unsaved changes in the current section: true = save, false = discard, null = stay.
    /// The argument is the section title shown to the user.
    /// </summary>
    public Func<string, bool?>? ConfirmSaveChanges { get; set; }

    public string SelectedSectionTitle => SectionTitle(SelectedSection);

    public static string SectionTitle(string section) =>
        "Settings › " + (section == SectionConditions ? "Device Specifications" : section);

    private static readonly TimeSpan SavedBadgeDuration = TimeSpan.FromSeconds(3);
    private readonly System.Windows.Threading.DispatcherTimer _savedTimer = new() { Interval = SavedBadgeDuration };

    /// <summary>Green check shown for a few seconds after a successful save.</summary>
    [ObservableProperty] private bool _isSavedBadgeVisible;

    private TaskCompletionSource? _savedBadgeHidden;

    private void ShowSavedBadge()
    {
        _savedTimer.Stop();
        IsSavedBadgeVisible = true;
        _savedTimer.Start();
    }

    private Task WaitForSavedBadgeAsync()
    {
        if (!IsSavedBadgeVisible)
        {
            return Task.CompletedTask;
        }

        _savedBadgeHidden ??= new TaskCompletionSource();
        return _savedBadgeHidden.Task;
    }

    partial void OnIsSavedBadgeVisibleChanged(bool value)
    {
        if (!value && _savedBadgeHidden is { } hidden)
        {
            _savedBadgeHidden = null;
            hidden.TrySetResult();
        }
    }

    private void OnConditionPropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshHasChanges();

    /// <summary>What the user can edit in a section, as JSON (monitor point icons are left out; they refresh in the background).</summary>
    private string SectionFingerprint(string section)
    {
        switch (section)
        {
            case SectionMonitorPoints:
                var points = JsonSerializer.SerializeToNode(CaptureMonitorPoints()) as JsonArray ?? [];
                foreach (var point in points.OfType<JsonObject>())
                {
                    point.Remove(nameof(MonitorPoint.Icon));
                }

                return points.ToJsonString();
            case SectionConditions:
                return JsonSerializer.Serialize(CaptureDeviceSpec()) + JsonSerializer.Serialize(Capture().Conditions);
            default:
                return JsonSerializer.Serialize(CaptureGeneral());
        }
    }

    private void MarkClean(string section)
    {
        _savedFingerprints[section] = SectionFingerprint(section);
        RefreshHasChanges();
    }

    private void MarkAllClean()
    {
        foreach (var section in Sections)
        {
            _savedFingerprints[section] = SectionFingerprint(section);
        }

        RefreshHasChanges();
    }

    private void RefreshHasChanges()
    {
        if (!_suspendChangeTracking)
        {
            HasChanges = !_savedFingerprints.TryGetValue(SelectedSection, out var saved)
                || SectionFingerprint(SelectedSection) != saved;
        }
    }

    partial void OnSelectedSectionChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedSectionTitle));
        RefreshHasChanges();
    }

    partial void OnStatusMessageChanged(string value)
    {
        _statusTimer.Stop();
        if (!string.IsNullOrEmpty(value))
        {
            _statusTimer.Start();
        }
    }

    /// <summary>
    /// Lets the user leave the current section: asks to save or discard its unsaved changes first.
    /// Returns false when the user chose to stay (or saving failed).
    /// </summary>
    public async Task<bool> TryLeaveSectionAsync()
    {
        if (SaveCommand.IsRunning || IsSavedBadgeVisible)
        {
            return false;
        }

        if (!HasChanges || ConfirmSaveChanges is null)
        {
            return true;
        }

        var choice = ConfirmSaveChanges(SelectedSectionTitle);
        if (choice == true)
        {
            await SaveCommand.ExecuteAsync(null);
            if (!HasChanges)
            {
                await WaitForSavedBadgeAsync();
            }
        }
        else if (choice == false)
        {
            ResetCommand.Execute(null);
        }

        return choice is not null && !HasChanges;
    }

    public ObservableCollection<InstalledAppInfo> InstalledApplications { get; } = [];

    private async Task LoadInstalledApplicationsAsync()
    {
        var apps = await Task.Run(InstalledProgramCatalog.Load);
        InstalledApplications.Clear();
        foreach (var app in apps)
        {
            InstalledApplications.Add(app);
        }

        foreach (var point in MonitorPoints)
        {
            point.RefreshApplicationIcon();
        }

        ApplicationsLoaded?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<MonitorPointAlertOption> AlertOptions { get; } =
    [
        new(MonitorPointAlert.Problem, "Problem", "AccentRedBrush"),
        new(MonitorPointAlert.Warning, "Warning", "AccentYellowBrush"),
        new(MonitorPointAlert.Unknown, "Unknown", "AccentGrayBrush")
    ];

    public IReadOnlyList<string> DiskUnitOptions { get; } = ["Percent", "GB"];

    public IReadOnlyList<YesNoOption> YesNoOptions { get; } = [new(true, "Yes"), new(false, "No")];

    public IReadOnlyList<MonitorPointAlertOption> SpecAlertOptions { get; } =
    [
        new(MonitorPointAlert.Problem, "Problem", "AccentRedBrush"),
        new(MonitorPointAlert.Warning, "Warning", "AccentYellowBrush")
    ];

    public ObservableCollection<string> OperatingSystemOptions { get; } =
    [
        "",
        "Windows 10",
        "Windows 11",
        "Windows Server 2016",
        "Windows Server 2019",
        "Windows Server 2022",
        "Windows Server 2025"
    ];

    public bool IsLightTheme
    {
        get => string.Equals(Theme, "Light", StringComparison.OrdinalIgnoreCase);
        set => Theme = value ? "Light" : "Dark";
    }

    partial void OnThemeChanged(string value) => OnPropertyChanged(nameof(IsLightTheme));

    public IReadOnlyList<MonitorPointTypeOption> MonitorPointTypes { get; } =
    [
        new(MonitorPointType.Website, MonitorPointTypeLabels.Format(MonitorPointType.Website)),
        new(MonitorPointType.Device, MonitorPointTypeLabels.Format(MonitorPointType.Device)),
        new(MonitorPointType.Application, MonitorPointTypeLabels.Format(MonitorPointType.Application)),
        new(MonitorPointType.Database, MonitorPointTypeLabels.Format(MonitorPointType.Database))
    ];

    public IReadOnlyList<DeviceKindOption> DeviceKinds { get; } =
        DeviceIcons.All.Select(item => new DeviceKindOption(item.Kind, item.Label, item.Glyph)).ToList();

    public IReadOnlyList<string> RuleOptions { get; } =
    [
        "Unreachable",
        "Response time (seconds)",
        "Process not running",
        "Connection failed"
    ];

    public IReadOnlyList<string> SeverityOptions { get; } = ["Warning", "Critical"];

    public ObservableCollection<MonitorPointSettingViewModel> MonitorPoints { get; } = [];

    public ObservableCollection<ConditionSettingViewModel> Conditions { get; } = [];

    public ObservableCollection<TargetOption> TargetOptions { get; } = [];

    public bool HasSelectedCondition => SelectedCondition is not null;

    [RelayCommand]
    private async Task SelectSection(string section)
    {
        if (section == SelectedSection)
        {
            return;
        }

        if (await TryLeaveSectionAsync())
        {
            SelectedSection = section;
        }
        else
        {
            // Staying: re-sync the side tabs, whose radio button already moved to the clicked section.
            OnPropertyChanged(nameof(SelectedSection));
        }
    }

    /// <summary>Saves only the selected section; the other sections keep their last saved values.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        var section = SelectedSection;
        try
        {
            StatusMessage = string.Empty;
            if (section == SectionMonitorPoints)
            {
                await CaptureIconsAsync();
            }

            var current = Capture();
            var saved = Clone(_snapshot);
            if (section == SectionGeneral)
            {
                if (!IsListenAddress(current.General.ServiceListenAddress))
                {
                    StatusMessage = "\"Service listens on\" must be an IPv4 address, such as 127.0.0.1, 0.0.0.0 or one of this computer's IPs.";
                    return;
                }

                if (current.General.ServicePort is < 1 or > 65535)
                {
                    StatusMessage = "The service port must be between 1 and 65535.";
                    return;
                }

                if (!ConfirmGeneralChanges(current.General, saved.General))
                {
                    return;
                }
            }

            var settings = new UiAppSettings
            {
                General = section == SectionGeneral ? current.General : saved.General,
                DeviceSpec = section == SectionConditions ? current.DeviceSpec : saved.DeviceSpec,
                Conditions = section == SectionConditions ? current.Conditions : saved.Conditions
            };
            var points = section == SectionMonitorPoints
                ? CaptureMonitorPoints()
                : _monitorPointSnapshot.Select(ClonePoint).ToList();

            await _store.SaveAsync(settings, points);
            if (section == SectionGeneral)
            {
                FollowServicePort(current.General, saved.General);
            }

            _snapshot = Clone(settings);
            _monitorPointSnapshot = points.Select(ClonePoint).ToList();
            SavePreferences();
            MarkClean(section);
            StatusMessage = section == SectionMonitorPoints && points.Any(point => point.Type == MonitorPointType.Website && string.IsNullOrWhiteSpace(point.Icon) && !string.IsNullOrWhiteSpace(point.Address))
                ? "No icon was found for one or more websites."
                : string.Empty;
            ShowSavedBadge();
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (IOException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    /// <summary>Puts the selected section back to its last saved values.</summary>
    [RelayCommand(CanExecute = nameof(HasChanges))]
    private void Reset()
    {
        var section = SelectedSection;
        _suspendChangeTracking = true;
        try
        {
            switch (section)
            {
                case SectionMonitorPoints:
                    ReplaceMonitorPoints(_monitorPointSnapshot);
                    break;
                case SectionConditions:
                    ApplyDeviceSpec(_snapshot.DeviceSpec);
                    ReplaceConditions(_snapshot.Conditions);
                    break;
                default:
                    ApplyGeneral(_snapshot.General);
                    break;
            }
        }
        finally
        {
            _suspendChangeTracking = false;
        }

        StatusMessage = string.Empty;
        MarkClean(section);
    }

    [RelayCommand]
    private void AddMonitorPoint()
    {
        var index = MonitorPoints.Count + 1;
        string id;
        do
        {
            id = $"point-{index++}";
        }
        while (MonitorPoints.Any(point => string.Equals(point.MonitorPointId, id, StringComparison.OrdinalIgnoreCase)));

        var point = new MonitorPointSettingViewModel
        {
            MonitorPointId = id,
            DisplayName = "New point",
            Type = MonitorPointType.Device,
            Enabled = true,
            IntervalSeconds = 3
        };
        point.PropertyChanged += OnMonitorPointPropertyChanged;
        MonitorPoints.Add(point);
        SelectedMonitorPoint = point;
        RefreshConditionTargets();
    }

    [RelayCommand]
    private void RemoveMonitorPoint(MonitorPointSettingViewModel? point)
    {
        var current = point ?? SelectedMonitorPoint;
        if (current is null || !MonitorPoints.Contains(current))
        {
            return;
        }

        var index = MonitorPoints.IndexOf(current);
        current.PropertyChanged -= OnMonitorPointPropertyChanged;
        MonitorPoints.Remove(current);
        SelectedMonitorPoint = MonitorPoints.Count == 0
            ? null
            : MonitorPoints[Math.Min(index, MonitorPoints.Count - 1)];

        foreach (var condition in Conditions.Where(condition => condition.TargetId == current.MonitorPointId))
        {
            condition.TargetId = AllMonitorPointsId;
        }

        RefreshConditionTargets();
    }

    [RelayCommand]
    private void AddCondition()
    {
        var condition = new ConditionSettingViewModel();
        Conditions.Add(condition);
        SelectedCondition = condition;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveCondition))]
    private void RemoveCondition()
    {
        var current = SelectedCondition;
        if (current is null)
        {
            return;
        }

        var index = Conditions.IndexOf(current);
        Conditions.Remove(current);
        SelectedCondition = Conditions.Count == 0
            ? null
            : Conditions[Math.Min(index, Conditions.Count - 1)];
    }

    private bool CanRemoveCondition() => SelectedCondition is not null;

    partial void OnSelectedConditionChanged(ConditionSettingViewModel? value)
    {
        RemoveConditionCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasSelectedCondition));
    }

    private void OnMonitorPointPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MonitorPointSettingViewModel.DisplayName) or nameof(MonitorPointSettingViewModel.MonitorPointId))
        {
            RefreshConditionTargets();
        }

        RefreshHasChanges();
    }

    partial void OnInternetIntervalSecondsChanged(int value) => NetworkIntervalSeconds = value;

    private void Apply(UiAppSettings settings)
    {
        ApplyGeneral(settings.General);
        ApplyDeviceSpec(settings.DeviceSpec);
        ReplaceConditions(settings.Conditions);
    }

    private void ApplyGeneral(GeneralSettings? settings)
    {
        var general = settings ?? new GeneralSettings();
        MachineName = string.IsNullOrWhiteSpace(general.MachineName) ? Environment.MachineName : general.MachineName.Trim();
        RefreshInterval = general.RefreshInterval < 1 ? DefaultRefreshInterval : general.RefreshInterval;
        InternetIntervalSeconds = AtLeastOne(general.InternetIntervalSeconds, DefaultInternetInterval);
        SpeedTestIntervalSeconds = Math.Max(0, general.SpeedTestIntervalSeconds);
        CpuIntervalSeconds = AtLeastOne(general.CpuIntervalSeconds, DefaultCpuInterval);
        RamIntervalSeconds = AtLeastOne(general.RamIntervalSeconds, DefaultRamInterval);
        NetworkIntervalSeconds = InternetIntervalSeconds;
        DiskIntervalSeconds = AtLeastOne(general.DiskIntervalSeconds, DefaultDiskInterval);
        HardwareOsIntervalSeconds = AtLeastOne(general.HardwareOsIntervalSeconds, DefaultHardwareOsInterval);
        Theme = general.Theme is "Dark" or "Light" ? general.Theme : DefaultTheme;
        NotificationsEnabled = general.NotificationsEnabled;
        DataRetentionDays = RetentionDays(general.DataRetentionDays);
        if (RetentionOptions.All(option => option.Days != DataRetentionDays))
        {
            DataRetentionDays = DefaultRetentionDays;
        }

        ServiceListenAddress = string.IsNullOrWhiteSpace(general.ServiceListenAddress) ? LocalOnlyAddress : general.ServiceListenAddress.Trim();
        AddListenOption(ServiceListenAddress);

        // 0 = the service's own default port, which is the one this app reached it on.
        ServicePort = general.ServicePort is > 0 and <= 65535
            ? general.ServicePort
            : AgentApiClient.ParseAddress(ApiBaseUrl)?.Port ?? DefaultServicePort;
        RemoteAccessKey = general.RemoteAccessKey?.Trim() ?? string.Empty;
    }

    private UiAppSettings Capture()
        => new()
        {
            General = CaptureGeneral(),
            Conditions = Conditions.Select(condition => new ConditionRecord
            {
                Name = condition.Name,
                TargetId = condition.TargetId,
                Rule = condition.Rule,
                Threshold = condition.Threshold,
                Severity = condition.Severity,
                Enabled = condition.Enabled
            }).ToList(),
            DeviceSpec = CaptureDeviceSpec()
        };

    private async Task CaptureIconsAsync()
    {
        foreach (var point in MonitorPoints)
        {
            if (point.Type == MonitorPointType.Application)
            {
                point.RefreshApplicationIcon();
                continue;
            }

            if (point.Type != MonitorPointType.Website || string.IsNullOrWhiteSpace(point.Address))
            {
                point.Icon = null;
                continue;
            }

            var previous = _monitorPointSnapshot.FirstOrDefault(item =>
                string.Equals(item.MonitorPointId, point.MonitorPointId, StringComparison.OrdinalIgnoreCase));
            if (previous is not null
                && previous.Type == MonitorPointType.Website
                && string.Equals(previous.Address.Trim(), point.Address.Trim(), StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(previous.Icon))
            {
                point.Icon = previous.Icon;
                continue;
            }

            point.Icon = await SiteLogoCache.CaptureAsync(point.Address);
        }
    }

    private List<MonitorPoint> CaptureMonitorPoints()
        => MonitorPoints.Select(point => point.ToModel()).ToList();

    private GeneralSettings CaptureGeneral()
        => new()
        {
            MachineName = string.IsNullOrWhiteSpace(MachineName) ? Environment.MachineName : MachineName.Trim(),
            RefreshInterval = AtLeastOne(RefreshInterval, DefaultRefreshInterval),
            InternetIntervalSeconds = AtLeastOne(InternetIntervalSeconds, DefaultInternetInterval),
            SpeedTestIntervalSeconds = Math.Max(0, SpeedTestIntervalSeconds),
            CpuIntervalSeconds = AtLeastOne(CpuIntervalSeconds, DefaultCpuInterval),
            RamIntervalSeconds = AtLeastOne(RamIntervalSeconds, DefaultRamInterval),
            NetworkIntervalSeconds = AtLeastOne(InternetIntervalSeconds, DefaultInternetInterval),
            DiskIntervalSeconds = AtLeastOne(DiskIntervalSeconds, DefaultDiskInterval),
            HardwareOsIntervalSeconds = AtLeastOne(HardwareOsIntervalSeconds, DefaultHardwareOsInterval),
            Theme = Theme is "Dark" or "Light" ? Theme : DefaultTheme,
            NotificationsEnabled = NotificationsEnabled,
            DataRetentionDays = RetentionDays(DataRetentionDays),
            ServiceListenAddress = string.IsNullOrWhiteSpace(ServiceListenAddress) ? LocalOnlyAddress : ServiceListenAddress.Trim(),
            ServicePort = ServicePort,
            RemoteAccessKey = RemoteAccessKey?.Trim() ?? string.Empty
        };

    private static GeneralSettings CloneGeneral(GeneralSettings? general)
        => new()
        {
            MachineName = string.IsNullOrWhiteSpace(general?.MachineName) ? Environment.MachineName : general.MachineName.Trim(),
            RefreshInterval = AtLeastOne(general?.RefreshInterval ?? 0, DefaultRefreshInterval),
            InternetIntervalSeconds = AtLeastOne(general?.InternetIntervalSeconds ?? 0, DefaultInternetInterval),
            SpeedTestIntervalSeconds = Math.Max(0, general?.SpeedTestIntervalSeconds ?? DefaultSpeedTestInterval),
            CpuIntervalSeconds = AtLeastOne(general?.CpuIntervalSeconds ?? 0, DefaultCpuInterval),
            RamIntervalSeconds = AtLeastOne(general?.RamIntervalSeconds ?? 0, DefaultRamInterval),
            NetworkIntervalSeconds = AtLeastOne(general?.NetworkIntervalSeconds ?? 0, DefaultNetworkInterval),
            DiskIntervalSeconds = AtLeastOne(general?.DiskIntervalSeconds ?? 0, DefaultDiskInterval),
            HardwareOsIntervalSeconds = AtLeastOne(general?.HardwareOsIntervalSeconds ?? 0, DefaultHardwareOsInterval),
            Theme = general?.Theme ?? DefaultTheme,
            NotificationsEnabled = general?.NotificationsEnabled ?? DefaultNotificationsEnabled,
            DataRetentionDays = RetentionDays(general?.DataRetentionDays ?? 0),
            ServiceListenAddress = general?.ServiceListenAddress ?? LocalOnlyAddress,
            ServicePort = general?.ServicePort ?? DefaultServicePort,
            RemoteAccessKey = general?.RemoteAccessKey ?? string.Empty
        };

    private void ApplyDeviceSpec(DeviceSpecSettings? spec)
    {
        spec ??= new DeviceSpecSettings();
        CpuMinCores = Math.Max(0, spec.CpuMinCores);
        CpuWarningPercent = Percent(spec.CpuWarningPercent);
        CpuProblemPercent = Percent(spec.CpuProblemPercent);
        RamMinGb = Math.Max(0, spec.RamMinGb);
        RamWarningPercent = Percent(spec.RamWarningPercent);
        RamProblemPercent = Percent(spec.RamProblemPercent);
        DiskUnit = UnitName(spec.DiskUnit);
        DiskMinimum = Math.Max(0, spec.DiskMinimum);
        DiskTotalWarning = Math.Max(0, spec.DiskTotalWarning > 0 ? spec.DiskTotalWarning : spec.DiskWarningPercent);
        DiskPartitionWarning = Math.Max(0, spec.DiskPartitionWarning);
        DiskPartitionUnit = UnitName(spec.DiskPartitionUnit);
        DiskRemainingWarningUnit = UnitName(spec.DiskRemainingWarningUnit);
        DiskRemainingWarning = DiskRemaining(spec.DiskRemainingWarning, DiskRemainingWarningUnit);
        DiskRemainingProblemUnit = UnitName(spec.DiskRemainingProblemUnit);
        DiskRemainingProblem = DiskRemaining(spec.DiskRemainingProblem, DiskRemainingProblemUnit);
        DownloadMinKbps = Math.Max(0, spec.DownloadMinKbps);
        UploadMinKbps = Math.Max(0, spec.UploadMinKbps);
        InternetMinKbps = Math.Max(0, spec.EffectiveInternetMinKbps);
        var operatingSystem = spec.OperatingSystem?.Trim() ?? string.Empty;
        if (operatingSystem.Length > 0 && !OperatingSystemOptions.Contains(operatingSystem))
        {
            OperatingSystemOptions.Add(operatingSystem);
        }

        RequiredOperatingSystem = operatingSystem;
        CpuAlert = SpecAlert(spec.CpuAlert);
        RamAlert = SpecAlert(spec.RamAlert);
        InternetNotify = spec.InternetNotify;
        DownloadNotify = spec.DownloadNotify;
        UploadNotify = spec.UploadNotify;
        DiskAlert = SpecAlert(spec.DiskAlert);
        OsNotify = spec.OsNotify;
    }

    private static MonitorPointAlert SpecAlert(MonitorPointAlert alert) =>
        alert == MonitorPointAlert.Warning ? MonitorPointAlert.Warning : MonitorPointAlert.Problem;

    public DeviceSpecSettings CaptureDeviceSpec()
        => new()
        {
            CpuMinCores = Math.Max(0, CpuMinCores),
            CpuWarningPercent = Percent(CpuWarningPercent),
            CpuProblemPercent = Percent(CpuProblemPercent),
            RamMinGb = Math.Max(0, RamMinGb),
            RamWarningPercent = Percent(RamWarningPercent),
            RamProblemPercent = Percent(RamProblemPercent),
            DiskUnit = UnitName(DiskUnit),
            DiskMinimum = Math.Max(0, DiskMinimum),
            DiskTotalWarning = Math.Max(0, DiskTotalWarning),
            DiskPartitionWarning = Math.Max(0, DiskPartitionWarning),
            DiskPartitionUnit = UnitName(DiskPartitionUnit),
            DiskRemainingWarningUnit = UnitName(DiskRemainingWarningUnit),
            DiskRemainingWarning = DiskRemaining(DiskRemainingWarning, DiskRemainingWarningUnit),
            DiskRemainingProblemUnit = UnitName(DiskRemainingProblemUnit),
            DiskRemainingProblem = DiskRemaining(DiskRemainingProblem, DiskRemainingProblemUnit),
            DownloadMinKbps = Math.Max(0, DownloadMinKbps),
            UploadMinKbps = Math.Max(0, UploadMinKbps),
            InternetMinKbps = Math.Max(0, InternetMinKbps),
            InternetMinMbps = Math.Max(0, InternetMinKbps) / 1000d,
            OperatingSystem = RequiredOperatingSystem.Trim(),
            CpuAlert = CpuAlert,
            RamAlert = RamAlert,
            InternetNotify = InternetNotify,
            DownloadNotify = DownloadNotify,
            UploadNotify = UploadNotify,
            DiskAlert = DiskAlert,
            OsNotify = OsNotify
        };

    private static DeviceSpecSettings CloneDeviceSpec(DeviceSpecSettings? spec)
        => new()
        {
            CpuMinCores = Math.Max(0, spec?.CpuMinCores ?? 0),
            CpuWarningPercent = Percent(spec?.CpuWarningPercent ?? 0),
            CpuProblemPercent = Percent(spec?.CpuProblemPercent ?? 0),
            RamMinGb = Math.Max(0, spec?.RamMinGb ?? 0),
            RamWarningPercent = Percent(spec?.RamWarningPercent ?? 0),
            RamProblemPercent = Percent(spec?.RamProblemPercent ?? 0),
            DiskUnit = UnitName(spec?.DiskUnit),
            DiskMinimum = Math.Max(0, spec?.DiskMinimum ?? 0),
            DiskTotalWarning = Math.Max(0, (spec?.DiskTotalWarning ?? 0) > 0 ? spec!.DiskTotalWarning : spec?.DiskWarningPercent ?? 0),
            DiskPartitionWarning = Math.Max(0, spec?.DiskPartitionWarning ?? 0),
            DiskPartitionUnit = UnitName(spec?.DiskPartitionUnit),
            DiskRemainingWarningUnit = UnitName(spec?.DiskRemainingWarningUnit),
            DiskRemainingWarning = DiskRemaining(spec?.DiskRemainingWarning ?? 0, UnitName(spec?.DiskRemainingWarningUnit)),
            DiskRemainingProblemUnit = UnitName(spec?.DiskRemainingProblemUnit),
            DiskRemainingProblem = DiskRemaining(spec?.DiskRemainingProblem ?? 0, UnitName(spec?.DiskRemainingProblemUnit)),
            DownloadMinKbps = Math.Max(0, spec?.DownloadMinKbps ?? 0),
            UploadMinKbps = Math.Max(0, spec?.UploadMinKbps ?? 0),
            InternetMinKbps = spec?.InternetMinKbps,
            InternetMinMbps = Math.Max(0, spec?.InternetMinMbps ?? 0),
            OperatingSystem = spec?.OperatingSystem?.Trim() ?? string.Empty,
            CpuAlert = spec?.CpuAlert ?? MonitorPointAlert.Problem,
            RamAlert = spec?.RamAlert ?? MonitorPointAlert.Problem,
            InternetNotify = spec?.InternetNotify ?? true,
            DownloadNotify = spec?.DownloadNotify ?? true,
            UploadNotify = spec?.UploadNotify ?? true,
            DiskAlert = spec?.DiskAlert ?? MonitorPointAlert.Problem,
            OsNotify = spec?.OsNotify ?? true
        };

    private static int Percent(int value) => Math.Clamp(value, 0, 100);

    private static double DiskRemaining(double value, string unit) =>
        unit == "GB" ? Math.Max(0, value) : Math.Clamp(value, 0, 100);

    private static string UnitName(string? value) =>
        value is "GB" or "Gigabytes" ? "GB" : "Percent";

    private static int AtLeastOne(int value, int fallback)
        => value < 1 ? fallback : value;

    private static UiAppSettings Clone(UiAppSettings settings)
        => new()
        {
            General = CloneGeneral(settings.General),
            Conditions = settings.Conditions.Select(condition => new ConditionRecord
            {
                Name = condition.Name,
                TargetId = condition.TargetId,
                Rule = condition.Rule,
                Threshold = condition.Threshold,
                Severity = condition.Severity,
                Enabled = condition.Enabled
            }).ToList(),
            DeviceSpec = CloneDeviceSpec(settings.DeviceSpec)
        };

    private static MonitorPoint ClonePoint(MonitorPoint point)
        => new()
        {
            MonitorPointId = point.MonitorPointId,
            DisplayName = point.DisplayName,
            Type = point.Type,
            DeviceKind = point.DeviceKind,
            Address = point.Address,
            Icon = point.Icon,
            Location = point.Location,
            Model = point.Model,
            Enabled = point.Enabled,
            ShowInShortcut = point.ShowInShortcut,
            Alert = point.Alert,
            Database = point.Database?.Copy(),
            IntervalSeconds = point.IntervalSeconds
        };

    private void ReplaceMonitorPoints(IEnumerable<MonitorPoint> points)
    {
        foreach (var point in MonitorPoints)
        {
            point.PropertyChanged -= OnMonitorPointPropertyChanged;
        }

        MonitorPoints.Clear();
        foreach (var point in points)
        {
            var row = MonitorPointSettingViewModel.From(point);
            row.PropertyChanged += OnMonitorPointPropertyChanged;
            MonitorPoints.Add(row);
        }

        SelectedMonitorPoint = MonitorPoints.FirstOrDefault();
        RefreshConditionTargets();
    }

    private void ReplaceConditions(IEnumerable<ConditionRecord> records)
    {
        Conditions.Clear();
        foreach (var record in records)
        {
            Conditions.Add(new ConditionSettingViewModel
            {
                Name = string.IsNullOrWhiteSpace(record.Name) ? "New condition" : record.Name,
                TargetId = string.IsNullOrWhiteSpace(record.TargetId) ? AllMonitorPointsId : record.TargetId,
                Rule = string.IsNullOrWhiteSpace(record.Rule) ? "Unreachable" : record.Rule,
                Threshold = string.IsNullOrWhiteSpace(record.Threshold) ? "5" : record.Threshold,
                Severity = record.Severity is "Warning" or "Critical" ? record.Severity : "Critical",
                Enabled = record.Enabled
            });
        }

        SelectedCondition = Conditions.FirstOrDefault();
    }

    private void RefreshConditionTargets()
    {
        if (TargetOptions.Count == 0 || TargetOptions[0].Id != AllMonitorPointsId)
        {
            TargetOptions.Insert(0, new TargetOption(AllMonitorPointsId, "All monitor points"));
        }
        else
        {
            TargetOptions[0].Label = "All monitor points";
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in MonitorPoints)
        {
            if (string.IsNullOrWhiteSpace(point.MonitorPointId))
            {
                continue;
            }

            ids.Add(point.MonitorPointId);
            var label = string.IsNullOrWhiteSpace(point.DisplayName) ? point.MonitorPointId : point.DisplayName.Trim();
            var existing = TargetOptions.FirstOrDefault(option => string.Equals(option.Id, point.MonitorPointId, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                TargetOptions.Add(new TargetOption(point.MonitorPointId, label));
            }
            else
            {
                existing.Label = label;
            }
        }

        for (var i = TargetOptions.Count - 1; i >= 1; i--)
        {
            if (!ids.Contains(TargetOptions[i].Id))
            {
                TargetOptions.RemoveAt(i);
            }
        }
    }
}

public static class MonitorPointTypeLabels
{
    public static string Format(MonitorPointType type) => type switch
    {
        MonitorPointType.Website => "Website/API",
        MonitorPointType.Device => "Device",
        MonitorPointType.Application => "Application",
        MonitorPointType.Database => "Database",
        _ => type.ToString()
    };
}

public sealed class DeviceKindOption(GarageDeviceKind kind, string label, string glyph)
{
    public GarageDeviceKind Kind { get; } = kind;

    public string Label { get; } = label;

    public string Glyph { get; } = glyph;
}

public sealed record RetentionOption(int Days, string Label);

public sealed record ListenOption(string Address, string Label);

public sealed class MonitorPointTypeOption(MonitorPointType type, string label)
{
    public MonitorPointType Type { get; } = type;

    public string Label { get; } = label;
}

public sealed partial class TargetOption : ObservableObject
{
    public TargetOption(string id, string label)
    {
        Id = id;
        _label = label;
    }

    public string Id { get; }

    [ObservableProperty] private string _label;
}
