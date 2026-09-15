using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClientAgent.UI.Enums;

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
}
