using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    [ObservableProperty] private bool _isHighlighted;

    [ObservableProperty] private bool _isOsHighlighted;

    [ObservableProperty] private bool _isMacHighlighted;

    [ObservableProperty] private bool _canCopy;

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
        CanCopy = (isHighlighted || isOsHighlighted || isMacHighlighted)
            && !string.Equals(Value, "-", StringComparison.Ordinal);
    }

    [RelayCommand]
    private void Copy() => TryCopy();

    public bool TryCopy()
    {
        if (!CanCopy)
        {
            return false;
        }

        try
        {
            Clipboard.SetText(Value);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
