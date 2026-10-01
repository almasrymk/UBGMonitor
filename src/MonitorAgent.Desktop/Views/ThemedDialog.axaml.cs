using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using MonitorAgent.UI.Services;

namespace MonitorAgent.Desktop.Views;

public enum DialogKind
{
    Warning,
    Question,
    Info
}

public enum DialogResult
{
    None,
    OK,
    Cancel,
    Yes,
    No
}

public sealed record DialogButton(string Label, DialogResult Result, bool IsPrimary = false, bool IsDanger = false);

/// <summary>Message box that follows the app theme (colors switch with light/dark mode).</summary>
public partial class ThemedDialog : Window
{
    private readonly DialogResult _cancelResult;
    private DialogResult _result;

    public ThemedDialog()
    {
        InitializeComponent();
    }

    private ThemedDialog(string title, string message, DialogKind kind, IReadOnlyList<DialogButton> buttons, DialogResult cancelResult)
        : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        _cancelResult = cancelResult;
        _result = cancelResult;

        (IconCircle.Fill, IconText.Text) = kind switch
        {
            DialogKind.Question => (UiTheme.Brush("AccentBlueBrush", Colors.DodgerBlue), "?"),
            DialogKind.Info => (UiTheme.Brush("AccentGreenBrush", Colors.Green), "i"),
            _ => (UiTheme.Brush("AccentOrangeBrush", Colors.Orange), "!")
        };

        foreach (var item in buttons)
        {
            var button = new Button
            {
                Content = item.Label,
                MinWidth = 90,
                Padding = new Thickness(16, 6),
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = item.IsPrimary,
                IsCancel = item.Result == cancelResult
            };
            button.Classes.Add("footer");
            if (item.IsDanger)
            {
                button.Classes.Add("danger");
            }
            else if (item.IsPrimary)
            {
                button.Classes.Add("accent");
            }

            button.Click += (_, _) =>
            {
                _result = item.Result;
                Close();
            };
            ButtonsPanel.Children.Add(button);
        }

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _result = _cancelResult;
                Close();
            }
        };
    }

    public static async Task<DialogResult> ShowAsync(Window? owner, string title, string message, DialogKind kind,
        IReadOnlyList<DialogButton> buttons, DialogResult cancelResult = DialogResult.Cancel)
    {
        var dialog = new ThemedDialog(title, message, kind, buttons, cancelResult);
        if (owner is { IsVisible: true })
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var closed = new TaskCompletionSource();
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.Show();
            await closed.Task;
        }

        return dialog._result;
    }

    private void Header_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        _result = _cancelResult;
        Close();
    }
}
