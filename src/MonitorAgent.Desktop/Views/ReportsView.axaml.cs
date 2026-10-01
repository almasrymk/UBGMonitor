using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.VisualTree;
using MonitorAgent.UI.ViewModels;

namespace MonitorAgent.Desktop.Views;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
    }

    private void Table_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is DataGrid { DataContext: ReportSectionViewModel section }
            && (e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true) is { DataContext: ReportRow row })
        {
            section.Open(row);
        }
    }
}

/// <summary>Builds a DataGrid's text columns from a list of headers; each row is a list of cell texts.</summary>
public sealed class ReportTable
{
    public static readonly AttachedProperty<IList<string>?> ColumnsProperty =
        AvaloniaProperty.RegisterAttached<ReportTable, DataGrid, IList<string>?>("Columns");

    static ReportTable()
    {
        ColumnsProperty.Changed.AddClassHandler<DataGrid>((grid, e) => Build(grid, e.NewValue as IList<string>));
    }

    public static IList<string>? GetColumns(DataGrid element) => element.GetValue(ColumnsProperty);

    public static void SetColumns(DataGrid element, IList<string>? value) => element.SetValue(ColumnsProperty, value);

    private static void Build(DataGrid grid, IList<string>? columns)
    {
        grid.AutoGenerateColumns = false;
        grid.Columns.Clear();
        if (columns is null)
        {
            return;
        }

        for (var i = 0; i < columns.Count; i++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = columns[i],
                Binding = new Binding($"[{i}]"),
                Width = new DataGridLength(1, i == columns.Count - 1 ? DataGridLengthUnitType.Star : DataGridLengthUnitType.Auto),
                MaxWidth = 520
            });
        }
    }
}
