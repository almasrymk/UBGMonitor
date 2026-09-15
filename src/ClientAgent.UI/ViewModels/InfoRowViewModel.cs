using CommunityToolkit.Mvvm.ComponentModel;
using ClientAgent.UI.Enums;

namespace ClientAgent.UI.ViewModels;

public sealed partial class InfoRowViewModel : ObservableObject
{
    public InfoRowViewModel(string label, SensorHealth health = SensorHealth.Ok)
    {
        Label = label;
        Health = health;
    }

    public string Label { get; }

    [ObservableProperty] private string _value = "-";

    [ObservableProperty] private SensorHealth _health = SensorHealth.Ok;

    public void Set(string value, SensorHealth health = SensorHealth.Ok)
    {
        Value = string.IsNullOrWhiteSpace(value) ? "-" : value;
        Health = health;
    }
}
