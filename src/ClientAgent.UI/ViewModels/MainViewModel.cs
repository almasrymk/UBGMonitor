using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClientAgent.UI.Services;

namespace ClientAgent.UI.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AgentApiClient _client;
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private string _agentStatus = "Connecting...";
    [ObservableProperty] private string _madkhalStatus = "Unknown";
    [ObservableProperty] private string _centralStatus = "Unknown";
    [ObservableProperty] private string _uptime = "-";
    [ObservableProperty] private long _pendingCount;
    [ObservableProperty] private string _lastUpdate = "Never";
    [ObservableProperty] private string _connectionMessage = string.Empty;
    [ObservableProperty] private bool _isPaused;

    public CpuViewModel Cpu { get; } = new();
    public RamViewModel Ram { get; } = new();
    public DiskViewModel Disk { get; } = new();
    public NetworkViewModel Network { get; } = new();

    public MainViewModel() : this(new AgentApiClient(new HttpClient()))
    {
    }

    public MainViewModel(AgentApiClient client)
    {
        _client = client;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
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
            var snapshotTask = _client.GetSnapshotAsync();
            var statusTask = _client.GetStatusAsync();
            await Task.WhenAll(snapshotTask, statusTask);

            var snapshot = snapshotTask.Result;
            var status = statusTask.Result;
            if (snapshot is not null)
            {
                Cpu.Update(snapshot.Cpu);
                Ram.Update(snapshot.Ram);
                Disk.Update(snapshot.Partitions);
                Network.Update(snapshot.Network);
            }

            if (status is not null)
            {
                AgentStatus = status.Status;
                MadkhalStatus = status.MadkhalConnected ? "Connected" : "Offline";
                CentralStatus = status.CentralConnected ? "Connected" : "Offline";
                Uptime = FormatUptime(status.Uptime);
                PendingCount = status.PendingCount;
            }

            ConnectionMessage = string.Empty;
            LastUpdate = DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception)
        {
            AgentStatus = "Service Not Responding";
            ConnectionMessage = "Service Not Responding";
        }
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

    private static string FormatUptime(TimeSpan value)
    {
        return value.TotalDays >= 1
            ? $"{(int)value.TotalDays}d {value.Hours}h {value.Minutes}m"
            : $"{value.Hours}h {value.Minutes}m {value.Seconds}s";
    }
}
