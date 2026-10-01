using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MonitorAgent.UI.Views;

public enum DialogKind
{
    Warning,
    Question,
    Info
}

public sealed record DialogButton(string Label, MessageBoxResult Result, bool IsPrimary = false, bool IsDanger = false);

/// <summary>Message box that follows the app theme (colors switch with light/dark mode).</summary>
public partial class ThemedDialog : Window
{
    private readonly MessageBoxResult _cancelResult;
    private MessageBoxResult _result;

    private ThemedDialog(string title, string message, DialogKind kind, IReadOnlyList<DialogButton> buttons, MessageBoxResult cancelResult)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        _cancelResult = cancelResult;
        _result = cancelResult;

        (IconCircle.Fill, IconText.Text) = kind switch
        {
            DialogKind.Question => ((Brush)FindResource("AccentBlueBrush"), "?"),
            DialogKind.Info => ((Brush)FindResource("AccentGreenBrush"), "i"),
            _ => ((Brush)FindResource("AccentOrangeBrush"), "!")
        };

        var footerStyle = (Style)FindResource("FooterButton");
        var accentStyle = (Style)FindResource("AccentFooterButton");
        foreach (var item in buttons)
        {
            var button = new Button
            {
                Content = item.Label,
                Style = item.IsPrimary || item.IsDanger ? accentStyle : footerStyle,
                MinWidth = 90,
                Padding = new Thickness(16, 6, 16, 6),
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = item.IsPrimary,
                IsCancel = item.Result == cancelResult
            };
            if (item.IsDanger)
            {
                button.Background = (Brush)FindResource("AccentRedBrush");
            }

            button.Click += (_, _) =>
            {
                _result = item.Result;
                Close();
            };
            ButtonsPanel.Children.Add(button);
        }

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _result = _cancelResult;
                Close();
            }
        };
    }

    public static MessageBoxResult Show(Window? owner, string title, string message, DialogKind kind,
        IReadOnlyList<DialogButton> buttons, MessageBoxResult cancelResult = MessageBoxResult.Cancel)
    {
        var dialog = new ThemedDialog(title, message, kind, buttons, cancelResult);
        if (owner is { IsLoaded: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
        return dialog._result;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _result = _cancelResult;
        Close();
    }
}
