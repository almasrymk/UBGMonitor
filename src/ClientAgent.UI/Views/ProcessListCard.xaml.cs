using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClientAgent.UI.Enums;
using ClientAgent.UI.ViewModels;

namespace ClientAgent.UI.Views;

public partial class ProcessListCard : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ProcessListCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty AccentColorProperty = DependencyProperty.Register(
        nameof(AccentColor), typeof(Brush), typeof(ProcessListCard), new PropertyMetadata(Brushes.White));

    public static readonly DependencyProperty SortByProperty = DependencyProperty.Register(
        nameof(SortBy), typeof(ProcessSortBy), typeof(ProcessListCard), new PropertyMetadata(ProcessSortBy.Cpu));

    public ProcessListCard()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public Brush AccentColor
    {
        get => (Brush)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public ProcessSortBy SortBy
    {
        get => (ProcessSortBy)GetValue(SortByProperty);
        set => SetValue(SortByProperty, value);
    }

    private void Processes_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<ListBoxItem>(e.OriginalSource as DependencyObject) is null)
        {
            return;
        }

        if (DataContext is ProcessListCardViewModel vm)
        {
            vm.OpenSelectedProcessCommand.Execute(null);
        }
    }

    private void Processes_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is ProcessListCardViewModel vm)
        {
            vm.SelectedItem = null;
        }
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}
