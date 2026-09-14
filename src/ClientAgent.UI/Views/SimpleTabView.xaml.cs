using System.Windows;
using System.Windows.Controls;

namespace ClientAgent.UI.Views;

public partial class SimpleTabView : UserControl
{
    public static readonly DependencyProperty TitleTextProperty = DependencyProperty.Register(
        nameof(TitleText), typeof(string), typeof(SimpleTabView), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BodyTextProperty = DependencyProperty.Register(
        nameof(BodyText), typeof(string), typeof(SimpleTabView), new PropertyMetadata(string.Empty));

    public SimpleTabView()
    {
        InitializeComponent();
    }

    public string TitleText
    {
        get => (string)GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public string BodyText
    {
        get => (string)GetValue(BodyTextProperty);
        set => SetValue(BodyTextProperty, value);
    }
}
