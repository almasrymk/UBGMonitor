using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using MonitorAgent.UI.Enums;
using MonitorAgent.UI.ViewModels;

namespace MonitorAgent.Desktop.Views;

public partial class ProcessListCard : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<ProcessListCard, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<IBrush?> AccentColorProperty =
        AvaloniaProperty.Register<ProcessListCard, IBrush?>(nameof(AccentColor), Brushes.White);

    public static readonly StyledProperty<ProcessSortBy> SortByProperty =
        AvaloniaProperty.Register<ProcessListCard, ProcessSortBy>(nameof(SortBy), ProcessSortBy.Cpu);

    public ProcessListCard()
    {
        InitializeComponent();
    }

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public IBrush? AccentColor { get => GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
    public ProcessSortBy SortBy { get => GetValue(SortByProperty); set => SetValue(SortByProperty, value); }

    private void Processes_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null
            && DataContext is ProcessListCardViewModel vm)
        {
            vm.OpenSelectedProcessCommand.Execute(null);
        }
    }

    private void Processes_PointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is ProcessListCardViewModel vm)
        {
            vm.SelectedItem = null;
        }
    }
}
