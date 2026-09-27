using CommunityToolkit.Mvvm.ComponentModel;

namespace ClientAgent.UI.ViewModels;

public sealed partial class ConditionSettingViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "New condition";
    [ObservableProperty] private string _targetId = "all";
    [ObservableProperty] private string _rule = "Unreachable";
    [ObservableProperty] private string _threshold = "5";
    [ObservableProperty] private string _severity = "Critical";
    [ObservableProperty] private bool _enabled = true;

    public bool IsResponseTime => Rule == "Response time (seconds)";

    public string Summary => $"{Rule} · {Severity}";

    partial void OnRuleChanged(string value)
    {
        OnPropertyChanged(nameof(IsResponseTime));
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnSeverityChanged(string value) => OnPropertyChanged(nameof(Summary));
}
