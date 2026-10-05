using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonitorAgent.UI.Enums;
using MonitorAgent.UI.Services;

namespace MonitorAgent.UI.ViewModels;

public sealed partial class InfoRowViewModel : ObservableObject
{
    public InfoRowViewModel(string label, SensorHealth health = SensorHealth.Ok)
    {
        Label = label;
        Health = health;
    }

    public string Label { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHealthDot))]
    private string _value = "-";

    [ObservableProperty] private SensorHealth _health = SensorHealth.Ok;

    [ObservableProperty] private bool _isHighlighted;

    [ObservableProperty] private bool _isOsHighlighted;

    [ObservableProperty] private bool _isMacHighlighted;

    [ObservableProperty] private bool _canCopy;

    /// <summary>True/false when the row has a Device Specifications minimum; null when no spec applies.</summary>
    [ObservableProperty] private bool? _specMet;

    /// <summary>Shows the spec ✓/✕ mark instead of the health dot.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHealthDot))]
    private bool _usesSpecMark;

    /// <summary>Health dot only for rows that actually have a reading.</summary>
    public bool ShowHealthDot => !UsesSpecMark && !string.Equals(Value, "-", StringComparison.Ordinal);

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

        return UiPlatform.SetClipboardText(Value);
    }
}
