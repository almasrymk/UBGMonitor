using CommunityToolkit.Mvvm.ComponentModel;

namespace ClientAgent.UI.ViewModels;

public sealed partial class PhysicalDiskRowViewModel : ObservableObject
{
    [ObservableProperty] private string _model = string.Empty;
    [ObservableProperty] private string _type = string.Empty;
    [ObservableProperty] private double _healthPercent = 100;
    [ObservableProperty] private string _temperature = "N/A";
    [ObservableProperty] private string _smartStatus = "Unknown";
}
