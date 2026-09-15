using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ClientAgent.Shared.Models;
using ClientAgent.UI.Enums;
using ClientAgent.UI.Services;

namespace ClientAgent.UI.ViewModels;

public sealed partial class HardwareOsViewModel : ObservableObject, IDisposable
{
    private readonly AgentApiClient _client;
    private readonly DispatcherTimer _staticTimer;
    private readonly DispatcherTimer _sensorsTimer;
    private readonly DispatcherTimer _networkTimer;

    [ObservableProperty] private bool _isLevel1Expanded;
    [ObservableProperty] private bool _isLevel2Expanded;
    [ObservableProperty] private bool _isLevel3Expanded;
    [ObservableProperty] private bool _isLevel4Expanded;
    [ObservableProperty] private bool _isLevel5Expanded;

    [ObservableProperty] private int _level1Count = 14;
    [ObservableProperty] private int _level2Count = 16;
    [ObservableProperty] private int _level3Count = 16;
    [ObservableProperty] private int _level4Count = 24;
    [ObservableProperty] private int _level5Count = 21;

    public ObservableCollection<InfoRowViewModel> Level1Rows { get; } = [];
    public ObservableCollection<InfoRowViewModel> Level2Rows { get; } = [];
    public ObservableCollection<InfoRowViewModel> Level3Rows { get; } = [];
    public ObservableCollection<InfoRowViewModel> Level4Rows { get; } = [];
    public ObservableCollection<InfoRowViewModel> Level5Rows { get; } = [];

    public HardwareOsViewModel(AgentApiClient client)
    {
        _client = client;
        _staticTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _sensorsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _networkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _staticTimer.Tick += async (_, _) => await RefreshStaticAsync();
        _sensorsTimer.Tick += async (_, _) => await RefreshLevelAsync(3);
        _networkTimer.Tick += async (_, _) => await RefreshLevelAsync(5);
        _staticTimer.Start();
        _ = RefreshStaticAsync();
    }

    partial void OnIsLevel3ExpandedChanged(bool value)
    {
        if (value)
        {
            _ = RefreshLevelAsync(3);
            _sensorsTimer.Start();
            return;
        }

        _sensorsTimer.Stop();
    }

    partial void OnIsLevel5ExpandedChanged(bool value)
    {
        if (value)
        {
            _ = RefreshLevelAsync(5);
            _networkTimer.Start();
            return;
        }

        _networkTimer.Stop();
    }

    private async Task RefreshStaticAsync()
    {
        try
        {
            var response = await _client.GetHardwareLevelsAsync();
            if (response?.Levels is null)
            {
                return;
            }

            foreach (var level in response.Levels)
            {
                Apply(level);
            }
        }
        catch
        {
            // Keep last known rows.
        }
    }

    private async Task RefreshLevelAsync(int level)
    {
        if (level == 3 && !IsLevel3Expanded)
        {
            return;
        }

        if (level == 5 && !IsLevel5Expanded)
        {
            return;
        }

        try
        {
            var dto = await _client.GetHardwareLevelAsync(level);
            if (dto is not null)
            {
                Apply(dto);
            }
        }
        catch
        {
            // Keep last known rows.
        }
    }

    private void Apply(HardwareLevelDto level)
    {
        var rows = level.Level switch
        {
            1 => Level1Rows,
            2 => Level2Rows,
            3 => Level3Rows,
            4 => Level4Rows,
            5 => Level5Rows,
            _ => null
        };
        if (rows is null)
        {
            return;
        }

        Replace(rows, level.Items ?? [], level.Level);
        switch (level.Level)
        {
            case 1: Level1Count = level.ItemCount; break;
            case 2: Level2Count = level.ItemCount; break;
            case 3: Level3Count = level.ItemCount; break;
            case 4: Level4Count = level.ItemCount; break;
            case 5: Level5Count = level.ItemCount; break;
        }
    }

    private static void Replace(ObservableCollection<InfoRowViewModel> rows, List<HardwareItemDto> items, int level)
    {
        if (rows.Count == items.Count && LabelsMatch(rows, items))
        {
            for (var i = 0; i < items.Count; i++)
            {
                ApplyRow(rows[i], items[i], level);
            }

            return;
        }

        rows.Clear();
        foreach (var item in items)
        {
            var row = new InfoRowViewModel(item.Name);
            ApplyRow(row, item, level);
            rows.Add(row);
        }
    }

    private static void ApplyRow(InfoRowViewModel row, HardwareItemDto item, int level)
    {
        var isMac = IsMacRow(item.Name);
        row.Set(
            item.Value,
            ToHealth(item.Status),
            IsNetworkIdentityRow(item.Name),
            IsOsIdentityRow(item.Name),
            isMac && level is 2 or 5);
    }

    private static bool LabelsMatch(ObservableCollection<InfoRowViewModel> rows, List<HardwareItemDto> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (!string.Equals(rows[i].Label, items[i].Name, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNetworkIdentityRow(string name)
        => name is "Public IP" or "Local IP" or "IPv4 Address";

    private static bool IsMacRow(string name)
        => name is "MAC Address";

    private static bool IsOsIdentityRow(string name)
        => name is "Windows Edition" or "Windows Version";

    private static SensorHealth ToHealth(string? status) => status switch
    {
        "Green" => SensorHealth.Ok,
        "Yellow" => SensorHealth.Warning,
        "Red" => SensorHealth.Critical,
        _ => SensorHealth.Unknown
    };

    public void Dispose()
    {
        _staticTimer.Stop();
        _sensorsTimer.Stop();
        _networkTimer.Stop();
    }
}
