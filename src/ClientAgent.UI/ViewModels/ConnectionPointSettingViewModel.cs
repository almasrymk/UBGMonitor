using CommunityToolkit.Mvvm.ComponentModel;
using ClientAgent.UI.Enums;

namespace ClientAgent.UI.ViewModels;

public sealed partial class ConnectionPointSettingViewModel : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty] private string _name = "New connection point";
    [ObservableProperty] private ConnectionPointKind _kind = ConnectionPointKind.Website;
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private int _intervalSeconds = 30;
    [ObservableProperty] private string _url = "https://";
    [ObservableProperty] private string _processName = "";
    [ObservableProperty] private string _executablePath = "";
    [ObservableProperty] private string _host = "";
    [ObservableProperty] private int _port = 1433;
    [ObservableProperty] private string _database = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _ipAddress = "";
    [ObservableProperty] private DeviceKind _deviceKind = DeviceKind.Camera;
    [ObservableProperty] private string _customDeviceType = "";

    public bool IsWebsite => Kind == ConnectionPointKind.Website;

    public bool IsServerProgram => Kind == ConnectionPointKind.ServerProgram;

    public bool IsDatabase => Kind is ConnectionPointKind.SqlServer or ConnectionPointKind.PostgreSql;

    public bool IsDevice => Kind == ConnectionPointKind.Device;

    public bool IsOtherDevice => IsDevice && DeviceKind == DeviceKind.Other;

    public string KindLabel => Kind switch
    {
        ConnectionPointKind.Website => "Website",
        ConnectionPointKind.ServerProgram => "Server Program",
        ConnectionPointKind.SqlServer => "SQL Server",
        ConnectionPointKind.PostgreSql => "PostgreSQL",
        ConnectionPointKind.Device => DeviceKind switch
        {
            DeviceKind.Camera => "Camera",
            DeviceKind.Gate => "Gate",
            DeviceKind.Dispenser => "Dispenser",
            DeviceKind.Other => string.IsNullOrWhiteSpace(CustomDeviceType) ? "Other device" : CustomDeviceType.Trim(),
            _ => "Device"
        },
        _ => Kind.ToString()
    };

    public string TypeHint => Kind switch
    {
        ConnectionPointKind.Website => "Watch a website and report when it stops responding.",
        ConnectionPointKind.ServerProgram => "Watch a program or service running on this server.",
        ConnectionPointKind.SqlServer => "Connect to a SQL Server database. Default port is 1433.",
        ConnectionPointKind.PostgreSql => "Connect to a PostgreSQL database. Default port is 5432.",
        ConnectionPointKind.Device => "Reach a garage device by IP, such as a camera, gate, dispenser, or another device.",
        _ => string.Empty
    };

    partial void OnKindChanged(ConnectionPointKind value)
    {
        if (value == ConnectionPointKind.SqlServer && Port == 5432)
        {
            Port = 1433;
        }
        else if (value == ConnectionPointKind.PostgreSql && Port == 1433)
        {
            Port = 5432;
        }

        NotifyPresentation();
    }

    partial void OnDeviceKindChanged(DeviceKind value) => NotifyPresentation();

    partial void OnCustomDeviceTypeChanged(string value) => OnPropertyChanged(nameof(KindLabel));

    private void NotifyPresentation()
    {
        OnPropertyChanged(nameof(IsWebsite));
        OnPropertyChanged(nameof(IsServerProgram));
        OnPropertyChanged(nameof(IsDatabase));
        OnPropertyChanged(nameof(IsDevice));
        OnPropertyChanged(nameof(IsOtherDevice));
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(TypeHint));
    }
}
