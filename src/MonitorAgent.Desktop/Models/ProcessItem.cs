using CommunityToolkit.Mvvm.ComponentModel;

namespace MonitorAgent.UI.Models;

public sealed partial class ProcessItem : ObservableObject
{
    [ObservableProperty] private int _rank;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private int _pid;
    [ObservableProperty] private double _value;
    [ObservableProperty] private string _unit = string.Empty;
    [ObservableProperty] private double _percent;
    [ObservableProperty] private string _displayValue = string.Empty;
    [ObservableProperty] private object? _icon;

    public void UpdateFrom(ProcessItem item)
    {
        Rank = item.Rank;
        Name = item.Name;
        Value = item.Value;
        Unit = item.Unit;
        Percent = item.Percent;
        DisplayValue = item.DisplayValue;
        Icon = item.Icon;
    }
}
