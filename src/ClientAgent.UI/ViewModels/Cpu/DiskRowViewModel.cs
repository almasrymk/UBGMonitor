using CommunityToolkit.Mvvm.ComponentModel;

namespace ClientAgent.UI.ViewModels;

public sealed partial class DiskRowViewModel : ObservableObject
{
    [ObservableProperty] private string _drive = string.Empty;
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private string _fileSystem = string.Empty;
    [ObservableProperty] private string _total = string.Empty;
    [ObservableProperty] private string _used = string.Empty;
    [ObservableProperty] private string _free = string.Empty;
    [ObservableProperty] private double _usagePercent;
    [ObservableProperty] private string _status = "Healthy";
}
