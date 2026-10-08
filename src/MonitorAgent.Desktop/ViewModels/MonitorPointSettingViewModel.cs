using CommunityToolkit.Mvvm.ComponentModel;
using MonitorAgent.Shared.Models;
using MonitorAgent.UI.Services;

namespace MonitorAgent.UI.ViewModels;

public sealed partial class MonitorPointSettingViewModel : ObservableObject
{
    [ObservableProperty] private string _monitorPointId = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private MonitorPointType _type = MonitorPointType.Device;
    [ObservableProperty] private GarageDeviceKind? _deviceKind = GarageDeviceKind.Camera;
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private string? _icon;
    [ObservableProperty] private string _location = string.Empty;
    [ObservableProperty] private string _model = string.Empty;
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private bool _showInShortcut;
    [ObservableProperty] private MonitorPointAlert _alert = MonitorPointAlert.Problem;
    [ObservableProperty] private int _intervalSeconds = 3;

    public string TypeLabel => MonitorPointTypeLabels.Format(Type);

    public bool IsDevice => Type == MonitorPointType.Device;

    public bool IsApplication => Type == MonitorPointType.Application;

    public bool IsDatabase => Type == MonitorPointType.Database;

    [ObservableProperty] private DatabaseLogin? _databaseLogin;

    public string DatabaseSummary
        => DatabaseLogin is null || string.IsNullOrWhiteSpace(DatabaseLogin.Server)
            ? "Configure"
            : $"{DatabaseEngineLabel(DatabaseLogin.Engine)} · {DatabaseLogin.Server}" + (DatabaseLogin.TlsMode == DatabaseTlsMode.Compatibility ? " · server identity not verified" : "");

    public string? DatabaseLogo => IsDatabase ? DatabaseLogos.Base64(DatabaseLogin?.Engine) : null;

    partial void OnDatabaseLoginChanged(DatabaseLogin? value)
    {
        OnPropertyChanged(nameof(DatabaseSummary));
        OnPropertyChanged(nameof(DatabaseLogo));
    }

    public string? ApplicationPath
    {
        get => IsApplication ? Address : null;
        set => ApplyApplication(value);
    }

    public string DeviceKindLabel => DeviceKind is GarageDeviceKind kind ? DeviceIcons.Label(kind) : string.Empty;

    public string DeviceGlyph => DeviceIcons.Glyph(Type, DeviceKind, DisplayName);

    partial void OnAddressChanged(string value)
    {
        if (Type == MonitorPointType.Website)
        {
            Icon = null;
        }

        OnPropertyChanged(nameof(ApplicationPath));
    }

    partial void OnTypeChanged(MonitorPointType value)
    {
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(IsDevice));
        OnPropertyChanged(nameof(IsApplication));
        OnPropertyChanged(nameof(IsDatabase));
        OnPropertyChanged(nameof(DatabaseLogo));
        OnPropertyChanged(nameof(ApplicationPath));
        OnPropertyChanged(nameof(DeviceGlyph));
        if (value is not (MonitorPointType.Website or MonitorPointType.Application))
        {
            Icon = null;
        }

        if (value == MonitorPointType.Device)
        {
            DeviceKind ??= GarageDeviceKind.Camera;
        }
        else
        {
            DeviceKind = null;
        }
    }

    partial void OnDeviceKindChanged(GarageDeviceKind? value)
    {
        OnPropertyChanged(nameof(DeviceKindLabel));
        OnPropertyChanged(nameof(DeviceGlyph));
    }

    partial void OnDisplayNameChanged(string value) => OnPropertyChanged(nameof(DeviceGlyph));

    private static string DatabaseEngineLabel(DatabaseEngine engine)
        => engine switch
        {
            DatabaseEngine.PostgreSql => "PostgreSQL",
            DatabaseEngine.MySql => "MySQL",
            _ => "SQL Server"
        };

    public void RefreshApplicationIcon()
    {
        if (Type != MonitorPointType.Application || string.IsNullOrWhiteSpace(Address))
        {
            return;
        }

        var icon = InstalledProgramCatalog.Find(Address)?.IconBase64;
        if (!string.IsNullOrWhiteSpace(icon))
        {
            Icon = icon;
        }
    }

    private void ApplyApplication(string? path)
    {
        if (Type != MonitorPointType.Application || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var changed = !string.Equals(Address, path, StringComparison.OrdinalIgnoreCase);
        Address = path;
        var app = InstalledProgramCatalog.Find(path);
        if (app is null)
        {
            return;
        }

        Model = app.ProcessName;
        if (!string.IsNullOrWhiteSpace(app.IconBase64))
        {
            Icon = app.IconBase64;
        }

        if (changed || string.IsNullOrWhiteSpace(DisplayName) || DisplayName == "New point")
        {
            DisplayName = app.Name;
        }
    }

    public static MonitorPointSettingViewModel From(MonitorPoint point)
        => new()
        {
            MonitorPointId = point.MonitorPointId,
            DisplayName = point.DisplayName,
            Type = point.Type,
            DeviceKind = point.Type == MonitorPointType.Device
                ? point.DeviceKind ?? GarageDeviceKind.Camera
                : null,
            Address = point.Address,
            Icon = point.Type is MonitorPointType.Website or MonitorPointType.Application ? point.Icon : null,
            Location = point.Location,
            Model = point.Model,
            Enabled = point.Enabled,
            ShowInShortcut = point.ShowInShortcut,
            Alert = point.Alert,
            DatabaseLogin = point.Database?.Copy(),
            IntervalSeconds = point.IntervalSeconds < 1 ? 3 : point.IntervalSeconds
        };

    public MonitorPoint ToModel()
        => new()
        {
            MonitorPointId = MonitorPointId.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? MonitorPointId.Trim() : DisplayName.Trim(),
            Type = Type,
            DeviceKind = Type == MonitorPointType.Device ? DeviceKind ?? GarageDeviceKind.Camera : null,
            Address = Address.Trim(),
            Icon = Type is MonitorPointType.Website or MonitorPointType.Application ? Icon : null,
            Location = Location.Trim(),
            Model = Model.Trim(),
            Enabled = Enabled,
            ShowInShortcut = ShowInShortcut,
            Alert = Alert,
            Database = Type == MonitorPointType.Database ? DatabaseLogin?.Copy() : null,
            IntervalSeconds = IntervalSeconds < 1 ? 3 : IntervalSeconds
        };
}
