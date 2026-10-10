using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MonitorAgent.Shared.Models;
using MonitorAgent.UI.Enums;
using MonitorAgent.UI.Services;

namespace MonitorAgent.UI.ViewModels;

public sealed partial class HardwareOsViewModel : ObservableObject, IDisposable
{
    private readonly AgentApiClient _client;
    private readonly UiTimer _staticTimer;
    private readonly UiTimer _sensorsTimer;
    private readonly UiTimer _networkTimer;
    public Func<bool>? IsActive { get; set; }

    [ObservableProperty] private bool _isLevel1Expanded;
    [ObservableProperty] private bool _isLevel2Expanded;
    [ObservableProperty] private bool _isLevel3Expanded;
    [ObservableProperty] private bool _isLevel4Expanded;
    [ObservableProperty] private bool _isLevel5Expanded;

    [ObservableProperty] private int _level1Count;
    [ObservableProperty] private int _level2Count;
    [ObservableProperty] private int _level3Count;
    [ObservableProperty] private int _level4Count;
    [ObservableProperty] private int _level5Count;

    public ObservableCollection<InfoRowViewModel> Level1Rows { get; } = [];
    public ObservableCollection<InfoRowViewModel> Level2Rows { get; } = [];
    public ObservableCollection<InfoRowViewModel> Level3Rows { get; } = [];
    public ObservableCollection<InfoRowViewModel> Level4Rows { get; } = [];
    public ObservableCollection<InfoRowViewModel> Level5Rows { get; } = [];

    public HardwareOsViewModel(AgentApiClient client)
    {
        _client = client;
        _staticTimer = new UiTimer(TimeSpan.FromSeconds(30));
        _sensorsTimer = new UiTimer(CollapsedSensorsInterval);
        _networkTimer = new UiTimer(TimeSpan.FromSeconds(3));
        _staticTimer.Tick += async (_, _) => { if (IsActive?.Invoke() != false) await RefreshStaticAsync(); };
        _sensorsTimer.Tick += async (_, _) => await RefreshLevelAsync(3);
        _networkTimer.Tick += async (_, _) => { if (IsActive?.Invoke() != false) await RefreshLevelAsync(5); };
        _staticTimer.Start();
        _sensorsTimer.Start();
        _ = RefreshStaticAsync();
        _ = RefreshLevelAsync(3);
    }

    /// <summary>Sensors keep refreshing while collapsed so temperature notifications still fire.</summary>
    private static readonly TimeSpan CollapsedSensorsInterval = TimeSpan.FromSeconds(10);

    partial void OnIsLevel3ExpandedChanged(bool value)
    {
        _sensorsTimer.Interval = value ? TimeSpan.FromSeconds(2) : CollapsedSensorsInterval;
        if (value)
        {
            _ = RefreshLevelAsync(3);
        }
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

    public void SetHardwareInterval(int seconds)
    {
        _staticTimer.Interval = TimeSpan.FromSeconds(seconds < 1 ? 30 : seconds);
    }

    public void SetNetworkInterval(int seconds)
    {
        _networkTimer.Interval = TimeSpan.FromSeconds(seconds < 1 ? 3 : seconds);
    }

    public Task RefreshStaticAsync() => RefreshStaticCoreAsync();

    public void Reset()
    {
        Level1Rows.Clear();
        Level2Rows.Clear();
        Level3Rows.Clear();
        Level4Rows.Clear();
        Level5Rows.Clear();
        Level1Count = 0;
        Level2Count = 0;
        Level3Count = 0;
        Level4Count = 0;
        Level5Count = 0;
    }

    private async Task RefreshStaticCoreAsync()
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

            // The static levels leave out Network; load it too so its count shows while collapsed.
            if (!IsLevel5Expanded)
            {
                await RefreshLevelAsync(5, force: true);
            }
        }
        catch
        {
            // Keep last known rows.
        }
    }

    private async Task RefreshLevelAsync(int level, bool force = false)
    {
        if (level == 5 && !IsLevel5Expanded && !force)
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
        row.UsesSpecMark = level is 1 or 2 or 4;
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
        => name.EndsWith(" Edition", StringComparison.Ordinal) || name.EndsWith(" Version", StringComparison.Ordinal);

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
