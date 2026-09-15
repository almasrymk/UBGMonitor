using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ClientAgent.Shared.Models;

namespace ClientAgent.UI.ViewModels;

public sealed partial class RamViewModel : ObservableObject
{
    [ObservableProperty] private double _usagePercent;
    [ObservableProperty] private double _totalGb;
    [ObservableProperty] private double _usedGb;
    [ObservableProperty] private double _freeGb;
    [ObservableProperty] private double _cachedGb;
    public ObservableCollection<double> History { get; } = [];

    public void Update(RamInfo ram)
    {
        UsagePercent = ram.UsagePercent;
        TotalGb = ram.TotalGB;
        UsedGb = ram.UsedGB;
        FreeGb = ram.FreeGB;
        CachedGb = ram.CachedGB;
        CpuViewModel.Push(History, ram.UsagePercent);
    }
}
