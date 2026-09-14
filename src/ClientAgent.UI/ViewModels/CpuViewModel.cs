using CommunityToolkit.Mvvm.ComponentModel;
using ClientAgent.Shared.Models;

namespace ClientAgent.UI.ViewModels;

public sealed partial class CpuViewModel : ObservableObject
{
    [ObservableProperty] private double _usagePercent;
    [ObservableProperty] private string _model = "Unknown";
    [ObservableProperty] private int _physicalCores;
    [ObservableProperty] private int _logicalCores;
    [ObservableProperty] private string _temperature = "N/A";

    public void Update(CpuInfo cpu)
    {
        UsagePercent = cpu.UsagePercent;
        Model = cpu.Model;
        PhysicalCores = cpu.PhysicalCores;
        LogicalCores = cpu.LogicalCores;
        Temperature = cpu.TemperatureC.HasValue ? $"{cpu.TemperatureC:0.0} °C" : "N/A";
    }
}
