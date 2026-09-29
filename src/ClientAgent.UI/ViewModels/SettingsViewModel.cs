using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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

    private readonly AppSettingsStore _store;
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
    [ObservableProperty] private string _theme = DefaultTheme;
    [ObservableProperty] private bool _notificationsEnabled = DefaultNotificationsEnabled;
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

    public SettingsViewModel() : this(new AppSettingsStore())
    {
    }

    public SettingsViewModel(AppSettingsStore store)
    {
        _store = store;
        TargetOptions.Add(new TargetOption(AllMonitorPointsId, "All monitor points"));
        Apply(_store.Load());
        ReplaceMonitorPoints(_store.LoadMonitorPoints());
        _snapshot = Capture();
        _monitorPointSnapshot = CaptureMonitorPoints();
        _ = LoadInstalledApplicationsAsync();
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
    private void SelectSection(string section) => SelectedSection = section;

    [RelayCommand]
    private async Task Save()
    {
        try
        {
            StatusMessage = "Saving...";
            await CaptureIconsAsync();
            var current = Capture();
            var points = CaptureMonitorPoints();
            _store.Save(current, points);
            _snapshot = Clone(current);
            _monitorPointSnapshot = points.Select(ClonePoint).ToList();
            StatusMessage = points.Any(point => point.Type == MonitorPointType.Website && string.IsNullOrWhiteSpace(point.Icon) && !string.IsNullOrWhiteSpace(point.Address))
                ? "Saved. No icon was found for one or more websites."
                : $"Saved to {_store.FilePath}";
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (UnauthorizedAccessException)
        {
            StatusMessage = $"Access denied to {_store.FilePath}. Run Agent Monitor as administrator or allow writing to the service folder.";
        }
        catch (IOException ex)
        {
            StatusMessage = $"Could not save service appsettings.json: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Reset()
    {
        Apply(_snapshot);
        ReplaceMonitorPoints(_monitorPointSnapshot);
        StatusMessage = string.Empty;
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

    [RelayCommand(CanExecute = nameof(CanRemoveMonitorPoint))]
    private void RemoveMonitorPoint()
    {
        var current = SelectedMonitorPoint;
        if (current is null)
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

    private bool CanRemoveMonitorPoint() => SelectedMonitorPoint is not null;

    private bool CanRemoveCondition() => SelectedCondition is not null;

    partial void OnSelectedMonitorPointChanged(MonitorPointSettingViewModel? value)
    {
        RemoveMonitorPointCommand.NotifyCanExecuteChanged();
    }

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
    }

    partial void OnInternetIntervalSecondsChanged(int value) => NetworkIntervalSeconds = value;

    private void Apply(UiAppSettings settings)
    {
        var general = settings.General ?? new GeneralSettings();
        MachineName = string.IsNullOrWhiteSpace(general.MachineName) ? Environment.MachineName : general.MachineName.Trim();
        RefreshInterval = general.RefreshInterval < 1 ? DefaultRefreshInterval : general.RefreshInterval;
        InternetIntervalSeconds = AtLeastOne(general.InternetIntervalSeconds, DefaultInternetInterval);
        SpeedTestIntervalSeconds = Math.Max(0, general.SpeedTestIntervalSeconds);
        CpuIntervalSeconds = AtLeastOne(general.CpuIntervalSeconds, DefaultCpuInterval);
        RamIntervalSeconds = AtLeastOne(general.RamIntervalSeconds, DefaultRamInterval);
        NetworkIntervalSeconds = InternetIntervalSeconds;
        DiskIntervalSeconds = AtLeastOne(general.DiskIntervalSeconds, DefaultDiskInterval);
        HardwareOsIntervalSeconds = AtLeastOne(general.HardwareOsIntervalSeconds, DefaultHardwareOsInterval);
        ApiBaseUrl = string.IsNullOrWhiteSpace(general.ApiBaseUrl) ? DefaultApiBaseUrl : general.ApiBaseUrl.Trim();
        Theme = general.Theme is "Dark" or "Light" ? general.Theme : DefaultTheme;
        NotificationsEnabled = general.NotificationsEnabled;
        ApplyDeviceSpec(settings.DeviceSpec);
        ReplaceConditions(settings.Conditions);
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
            ApiBaseUrl = string.IsNullOrWhiteSpace(ApiBaseUrl) ? DefaultApiBaseUrl : ApiBaseUrl.Trim(),
            Theme = Theme is "Dark" or "Light" ? Theme : DefaultTheme,
            NotificationsEnabled = NotificationsEnabled
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
            ApiBaseUrl = general?.ApiBaseUrl ?? DefaultApiBaseUrl,
            Theme = general?.Theme ?? DefaultTheme,
            NotificationsEnabled = general?.NotificationsEnabled ?? DefaultNotificationsEnabled
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
