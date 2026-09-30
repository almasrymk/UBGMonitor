using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ClientAgent.UI.ViewModels;

namespace ClientAgent.UI.Views;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
    }

    /// <summary>Scrolls the page instead of the table once the table reaches its top or bottom.</summary>
    private void Table_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var inner = FindChild<ScrollViewer>((DependencyObject)sender);
        var atEdge = inner is null
            || inner.ScrollableHeight == 0
            || (e.Delta > 0 && inner.VerticalOffset <= 0)
            || (e.Delta < 0 && inner.VerticalOffset >= inner.ScrollableHeight);
        if (!atEdge)
        {
            return;
        }

        e.Handled = true;
        var parent = VisualTreeHelper.GetParent((DependencyObject)sender) as UIElement;
        parent?.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = sender
        });
    }

    private void Table_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { DataContext: ReportSectionViewModel section } grid
            && ItemsControl.ContainerFromElement(grid, (DependencyObject)e.OriginalSource) is DataGridRow { Item: ReportRow row })
        {
            section.Open(row);
        }
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            if (FindChild<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}

/// <summary>Builds a DataGrid's text columns from a list of headers; each row is a list of cell texts.</summary>
public static class ReportTable
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.RegisterAttached(
        "Columns", typeof(IList<string>), typeof(ReportTable), new PropertyMetadata(null, OnColumnsChanged));

    public static IList<string>? GetColumns(DependencyObject element) => (IList<string>?)element.GetValue(ColumnsProperty);

    public static void SetColumns(DependencyObject element, IList<string>? value) => element.SetValue(ColumnsProperty, value);

    private static void OnColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
        {
            return;
        }

        grid.AutoGenerateColumns = false;
        grid.Columns.Clear();
        if (e.NewValue is not IList<string> columns)
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
