using System.Windows.Media;
using ClientAgent.Shared.Models;

namespace ClientAgent.UI.Models;

public sealed class MonitorPointAlertOption
{
    public MonitorPointAlertOption(MonitorPointAlert alert, string label, string brushKey)
    {
        Alert = alert;
        Label = label;
        Brush = System.Windows.Application.Current?.TryFindResource(brushKey) as Brush ?? Brushes.Gray;
    }

    public MonitorPointAlert Alert { get; }

    public string Label { get; }

    public Brush Brush { get; }
}
