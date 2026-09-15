using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ClientAgent.Shared.Models;

namespace ClientAgent.UI.ViewModels;

public sealed partial class CpuViewModel : ObservableObject
{
    [ObservableProperty] private double _usagePercent;
    [ObservableProperty] private string _model = "Unknown";
    [ObservableProperty] private int _physicalCores;
    [ObservableProperty] private int _logicalCores;
    [ObservableProperty] private string _coresText = "-";
    [ObservableProperty] private string _speedText = "-";
    [ObservableProperty] private string _temperature = "N/A";
    [ObservableProperty] private int _processCount;
    public ObservableCollection<double> History { get; } = [];

    public void Update(CpuInfo cpu)
    {
        UsagePercent = cpu.UsagePercent;
        Model = cpu.Model;
        PhysicalCores = cpu.PhysicalCores;
        LogicalCores = cpu.LogicalCores;
        CoresText = $"{cpu.LogicalCores} ({cpu.PhysicalCores}P)";
        SpeedText = $"{cpu.CurrentSpeedGhz:0.0} GHz (Max {cpu.MaxSpeedGhz:0.0})";
        Temperature = cpu.TemperatureC.HasValue ? $"{cpu.TemperatureC:0}°C" : "N/A";
        ProcessCount = cpu.ProcessCount;
        Push(History, cpu.UsagePercent);
    }

    internal static void Push(ObservableCollection<double> history, double value)
    {
        history.Add(value);
        while (history.Count > 20)
        {
            history.RemoveAt(0);
        }
    }
}
