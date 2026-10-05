using MonitorAgent.Shared.Models;
using MonitorAgent.UI.Services;

namespace MonitorAgent.UI.Models;

public sealed class MonitorPointAlertOption
{
    public MonitorPointAlertOption(MonitorPointAlert alert, string label, string brushKey)
    {
        Alert = alert;
        Label = label;
        Brush = UiTheme.Resource(brushKey, "#808080");
    }

    public MonitorPointAlert Alert { get; }

    public string Label { get; }

    /// <summary>The UI framework's brush.</summary>
    public object Brush { get; }
}
