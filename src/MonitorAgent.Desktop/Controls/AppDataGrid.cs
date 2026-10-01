using Avalonia.Controls;

namespace MonitorAgent.Desktop.Controls;

/// <summary>
/// DataGrid with alternating rows (the "alt" class on odd rows) whose fixed-width columns keep their width,
/// so a wide grid scrolls sideways instead of squeezing them to fit a star column.
/// </summary>
public class AppDataGrid : DataGrid
{
    public AppDataGrid()
    {
        LoadingRow += (_, e) => e.Row.Classes.Set("alt", e.Row.Index % 2 == 1);
    }

    protected override Type StyleKeyOverride => typeof(DataGrid);

    protected override void OnInitialized()
    {
        base.OnInitialized();
        foreach (var column in Columns)
        {
            if (column.Width.IsAbsolute && column.MinWidth < column.Width.Value)
            {
                column.MinWidth = column.Width.Value;
            }
        }
    }
}
