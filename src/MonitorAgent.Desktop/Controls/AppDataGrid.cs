using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace MonitorAgent.Desktop.Controls;

/// <summary>
/// DataGrid with alternating rows (the "alt" class on odd rows) that gives up column width the way the
/// Windows app's grid does when the columns don't fit: star columns go down to their minimum width and every
/// fixed column gives up the same amount, each stopping at its minimum, so a scroll bar only shows once every
/// column is at its minimum. Avalonia's own grid shrinks the last columns first instead.
/// </summary>
public class AppDataGrid : DataGrid
{
    private readonly Dictionary<DataGridColumn, double> _widths = new();
    private ScrollBar? _verticalScrollBar;
    private double _fittedWidth = double.NaN;

    public AppDataGrid()
    {
        LoadingRow += (_, e) => { e.Row.Classes.Set("alt", e.Row.Index % 2 == 1); AddSortHeaders(); };

        // The column edges are snapped in display order, so a moved column needs them worked out again.
        ColumnReordered += (_, _) =>
        {
            _fittedWidth = double.NaN;
            InvalidateMeasure();
        };
    }

    protected override Type StyleKeyOverride => typeof(DataGrid);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        AddSortHeaders();
        if (_verticalScrollBar is not null)
        {
            _verticalScrollBar.PropertyChanged -= OnScrollBarPropertyChanged;
        }

        _verticalScrollBar = e.NameScope.Find<ScrollBar>("PART_VerticalScrollbar");
        if (_verticalScrollBar is not null)
        {
            _verticalScrollBar.PropertyChanged += OnScrollBarPropertyChanged;
        }
    }

    private void AddSortHeaders()
    {
        foreach (var column in Columns.Where(c => c.CanUserSort && c.Header is not null && c.HeaderTemplate is null))
        {
            column.HeaderTemplate = new FuncDataTemplate<object>((header, _) =>
            {
                var arrows = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.65 };
                foreach (var geometry in new[] { "M 0,3 L 3,0 L 6,3 Z", "M 0,0 L 3,3 L 6,0 Z" })
                    arrows.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(geometry), Width = 6, Height = 3, Fill = Brushes.Gray });
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                panel.Children.Add(new TextBlock { Text = header?.ToString(), VerticalAlignment = VerticalAlignment.Center });
                panel.Children.Add(arrows);
                return panel;
            });
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width;
        if (_verticalScrollBar is { IsVisible: true })
        {
            width -= double.IsNaN(_verticalScrollBar.Width) ? _verticalScrollBar.Bounds.Width : _verticalScrollBar.Width;
        }

        if (!double.IsInfinity(width) && !width.Equals(_fittedWidth))
        {
            _fittedWidth = width;
            FitColumns(width);
        }

        return base.MeasureOverride(availableSize);
    }

    private void OnScrollBarPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
        {
            _fittedWidth = double.NaN;
            InvalidateMeasure();
        }
    }

    private void FitColumns(double width)
    {
        var columns = Columns.Where(column => column.IsVisible).ToList();
        foreach (var column in columns.Where(column => column.Width.IsAbsolute && !_widths.ContainsKey(column)))
        {
            _widths[column] = column.Width.Value;
        }

        var fixedColumns = columns.Where(_widths.ContainsKey).ToList();
        var space = width - columns.Where(column => !_widths.ContainsKey(column))
            .Sum(column => column.Width.IsStar ? MinimumWidth(column) : column.ActualWidth);

        var cut = columns.Any(column => column.Width.IsStar) ? Cut(fixedColumns, space) : 0;
        var targets = fixedColumns.ToDictionary(column => column, column => Math.Max(MinimumWidth(column), _widths[column] - cut));
        SnapToPixels(columns, targets, width, space);

        foreach (var (column, target) in targets)
        {
            if (!column.Width.Value.Equals(target))
            {
                column.Width = new DataGridLength(target);
            }
        }
    }

    /// <summary>
    /// Puts every column edge on the pixel nearest to it, as the Windows grid does. Avalonia rounds each column
    /// up from an edge that is not rounded itself, so at 125% an 80 wide column after a 55 wide one came out a
    /// pixel wider and every later column moved over.
    /// </summary>
    private void SnapToPixels(List<DataGridColumn> columns, Dictionary<DataGridColumn, double> targets, double width, double space)
    {
        if (!UseLayoutRounding)
        {
            return;
        }

        // A single star column gets what the others leave, so the edges after it are known too.
        var stars = columns.Where(column => column.Width.IsStar).ToList();
        double? starWidth = stars.Count == 1 && columns.All(column => targets.ContainsKey(column) || column.Width.IsStar)
            ? Math.Max(MinimumWidth(stars[0]), width - targets.Values.Sum())
            : null;

        var scale = LayoutHelper.GetLayoutScale(this);
        var snapped = new Dictionary<DataGridColumn, double>();
        DataGridColumn? last = null;
        double edge = 0, snappedEdge = 0;
        foreach (var column in columns.OrderBy(column => column.DisplayIndex))
        {
            var isFixed = targets.TryGetValue(column, out var columnWidth);
            if (!isFixed)
            {
                if (!column.Width.IsStar || starWidth is not { } star)
                {
                    break;
                }

                columnWidth = star;
            }

            edge += columnWidth;
            var next = Math.Round(edge * scale) / scale;
            if (isFixed)
            {
                snapped[column] = next - snappedEdge;
                last = column;
            }

            snappedEdge = next;
        }

        // Squeezed columns fill the space exactly, so rounding the last edge up would overflow it.
        if (last is not null && snapped.Values.Sum() > space + 1e-6)
        {
            snapped[last] -= 1 / scale;
        }

        if (snapped.Values.Sum() <= space + 1e-6)
        {
            foreach (var (column, value) in snapped)
            {
                targets[column] = value;
            }
        }
    }

    /// <summary>How much to take off every fixed column, each stopping at its minimum, so that all of them fit in the space.</summary>
    private double Cut(List<DataGridColumn> columns, double space)
    {
        double Total(double cut) => columns.Sum(column => Math.Max(MinimumWidth(column), _widths[column] - cut));

        if (columns.Count == 0 || Total(0) <= space)
        {
            return 0;
        }

        double low = 0, high = columns.Max(column => _widths[column]);
        for (var i = 0; i < 50; i++)
        {
            var middle = (low + high) / 2;
            if (Total(middle) > space)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return high;
    }

    private double MinimumWidth(DataGridColumn column) => Math.Max(column.MinWidth, MinColumnWidth);
}
