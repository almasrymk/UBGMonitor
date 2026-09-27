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
    private const string DefaultApiBaseUrl = "http://127.0.0.1:5050";
    private const string DefaultTheme = "Dark";
    private const bool DefaultNotificationsEnabled = true;

    private readonly AppSettingsStore _store;
    private UiAppSettings _snapshot = new();
    private List<MonitorPoint> _monitorPointSnapshot = [];

    [ObservableProperty] private string _selectedSection = SectionGeneral;
    [ObservableProperty] private int _refreshInterval = DefaultRefreshInterval;
    [ObservableProperty] private string _apiBaseUrl = DefaultApiBaseUrl;
    [ObservableProperty] private string _theme = DefaultTheme;
    [ObservableProperty] private bool _notificationsEnabled = DefaultNotificationsEnabled;
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

    public IReadOnlyList<string> ThemeOptions { get; } = ["Dark", "Light"];

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
                : "Saved to service appsettings.json";
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Could not save service appsettings.json";
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

    private void Apply(UiAppSettings settings)
    {
        var general = settings.General ?? new GeneralSettings();
        RefreshInterval = general.RefreshInterval < 1 ? DefaultRefreshInterval : general.RefreshInterval;
        ApiBaseUrl = string.IsNullOrWhiteSpace(general.ApiBaseUrl) ? DefaultApiBaseUrl : general.ApiBaseUrl.Trim();
        Theme = general.Theme is "Dark" or "Light" ? general.Theme : DefaultTheme;
        NotificationsEnabled = general.NotificationsEnabled;
        ReplaceConditions(settings.Conditions);
    }

    private UiAppSettings Capture()
        => new()
        {
            General = new GeneralSettings
            {
                RefreshInterval = RefreshInterval < 1 ? DefaultRefreshInterval : RefreshInterval,
                ApiBaseUrl = string.IsNullOrWhiteSpace(ApiBaseUrl) ? DefaultApiBaseUrl : ApiBaseUrl.Trim(),
                Theme = Theme is "Dark" or "Light" ? Theme : DefaultTheme,
                NotificationsEnabled = NotificationsEnabled
            },
            Conditions = Conditions.Select(condition => new ConditionRecord
            {
                Name = condition.Name,
                TargetId = condition.TargetId,
                Rule = condition.Rule,
                Threshold = condition.Threshold,
                Severity = condition.Severity,
                Enabled = condition.Enabled
            }).ToList()
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

    private static UiAppSettings Clone(UiAppSettings settings)
        => new()
        {
            General = new GeneralSettings
            {
                RefreshInterval = settings.General?.RefreshInterval ?? DefaultRefreshInterval,
                ApiBaseUrl = settings.General?.ApiBaseUrl ?? DefaultApiBaseUrl,
                Theme = settings.General?.Theme ?? DefaultTheme,
                NotificationsEnabled = settings.General?.NotificationsEnabled ?? DefaultNotificationsEnabled
            },
            Conditions = settings.Conditions.Select(condition => new ConditionRecord
            {
                Name = condition.Name,
                TargetId = condition.TargetId,
                Rule = condition.Rule,
                Threshold = condition.Threshold,
                Severity = condition.Severity,
                Enabled = condition.Enabled
            }).ToList()
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
