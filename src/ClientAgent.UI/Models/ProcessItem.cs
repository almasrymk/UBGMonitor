using System.Windows.Media;

namespace ClientAgent.UI.Models;

public sealed class ProcessItem
{
    public int Rank { get; init; }

    public string Name { get; init; } = string.Empty;

    public int Pid { get; init; }

    public double Value { get; init; }

    public string Unit { get; init; } = string.Empty;

    public double Percent { get; init; }

    public string DisplayValue { get; init; } = string.Empty;

    public ImageSource? Icon { get; init; }
}
