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

    public void Set(
        string value,
        SensorHealth health = SensorHealth.Ok,
        bool isHighlighted = false,
        bool isOsHighlighted = false,
        bool isMacHighlighted = false)
    {
        Value = string.IsNullOrWhiteSpace(value) ? "-" : value;
        Health = health;
        IsHighlighted = isHighlighted;
        IsOsHighlighted = isOsHighlighted;
        IsMacHighlighted = isMacHighlighted;
    }

    [ObservableProperty] private bool _isHighlighted;

    [ObservableProperty] private bool _isOsHighlighted;

    [ObservableProperty] private bool _isMacHighlighted;
}
