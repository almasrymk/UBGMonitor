using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClientAgent.UI.Services;
using ClientAgent.UI.ViewModels;
using ClientAgent.UI.Views;

namespace ClientAgent.UI;

public partial class MainWindow : Window
{
    private const double PinnedNavWidth = 92;
    private const double StripSize = 16;

    private static readonly string PanelsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgentMonitor",
        "panels.json");

    private sealed record PanelPins(
        bool NavPinned = true,
        bool StatusPinned = true,
        bool? DefaultNavPinned = null,
        bool? DefaultStatusPinned = null);

    private PanelPins _pins = new();

    private readonly AutoHidePanel _nav;
    private readonly AutoHidePanel _status;

    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        viewModel.Settings.ConfirmSaveChanges = section => ThemedDialog.Show(
                this,
                "Unsaved changes",
                $"You have unsaved changes in \"{section}\".\n\nDo you want to save them before leaving this tab?",
                DialogKind.Question,
                [
                    new DialogButton("Save", MessageBoxResult.Yes, IsPrimary: true),
                    new DialogButton("Don't save", MessageBoxResult.No),
                    new DialogButton("Cancel", MessageBoxResult.Cancel)
                ]) switch
            {
                MessageBoxResult.Yes => true,
                MessageBoxResult.No => false,
                _ => null
            };
        viewModel.Settings.ConfirmWarning = message => ThemedDialog.Show(
                this,
                "Warning",
                message,
                DialogKind.Warning,
                [
                    new DialogButton("Continue", MessageBoxResult.Yes),
                    new DialogButton("Cancel", MessageBoxResult.Cancel, IsPrimary: true)
                ]) == MessageBoxResult.Yes;
        DataContext = viewModel;
        StateChanged += (_, _) => MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "☐";
        Loaded += (_, _) => FitToWorkArea();

        _nav = new AutoHidePanel(NavPanel, NavStrip, NavShift, horizontal: true, pinned =>
        {
            NavColumn.Width = new GridLength(pinned ? PinnedNavWidth : StripSize);
            Grid.SetColumnSpan(NavPanel, pinned ? 1 : 2);
        });
        _status = new AutoHidePanel(StatusPanel, StatusStrip, StatusShift, horizontal: false, pinned =>
        {
            StatusRow.Height = pinned ? GridLength.Auto : new GridLength(StripSize);
            Grid.SetRowSpan(StatusPanel, pinned ? 1 : 2);
        });

        _pins = LoadPins();
        ApplyPins(_pins.NavPinned, _pins.StatusPinned);

        StatusPin.Click += (_, _) => { _status.SetPinned(StatusPin.IsChecked == true); SavePins(); };
        NavPanel.PreviewMouseLeftButtonUp += (_, _) => Dispatcher.BeginInvoke(_nav.Close);

        PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.XButton1 && GoBack())
            {
                e.Handled = true;
            }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.SystemKey == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt && GoBack())
            {
                e.Handled = true;
            }
        };
    }

    private bool GoBack()
    {
        if (DataContext is not MainViewModel vm || !vm.GoBackCommand.CanExecute(null))
        {
            return false;
        }

        vm.GoBackCommand.Execute(null);
        return true;
    }

    private void ApplyPins(bool navPinned, bool statusPinned)
    {
        StatusPin.IsChecked = statusPinned;
        _nav.SetPinned(navPinned);
        _status.SetPinned(statusPinned);
    }

    private void ToggleMenu_Click(object sender, RoutedEventArgs e)
    {
        _nav.SetPinned(!_nav.Pinned);
        SavePins();
    }

    private void ResetLayout_Click(object sender, RoutedEventArgs e)
    {
        Dashboard.ResetLayout(useMyDefault: true);
        ApplyPins(_pins.DefaultNavPinned ?? true, _pins.DefaultStatusPinned ?? true);
        SavePins();
    }

    private void ResetOriginalLayout_Click(object sender, RoutedEventArgs e)
    {
        Dashboard.ResetLayout(useMyDefault: false);
        ApplyPins(true, true);
        SavePins();
    }

    private void SaveDefaultLayout_Click(object sender, RoutedEventArgs e)
    {
        Dashboard.SaveCurrentAsMyDefault();
        _pins = _pins with { DefaultNavPinned = _nav.Pinned, DefaultStatusPinned = _status.Pinned };
        SavePins();
        ThemedDialog.Show(this, "Reset layout", "The current layout is saved as your default. \"Reset layout\" will return to it.",
            DialogKind.Info, [new DialogButton("OK", MessageBoxResult.OK, IsPrimary: true)], MessageBoxResult.OK);
    }

    private static PanelPins LoadPins()
    {
        try
        {
            return File.Exists(PanelsPath)
                ? JsonSerializer.Deserialize<PanelPins>(File.ReadAllText(PanelsPath)) ?? new PanelPins()
                : new PanelPins();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new PanelPins();
        }
    }

    private void SavePins()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PanelsPath)!);
            _pins = _pins with { NavPinned = _nav.Pinned, StatusPinned = _status.Pinned };
            File.WriteAllText(PanelsPath, JsonSerializer.Serialize(_pins));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pin state is a convenience; keep the current session state.
        }
    }

    private void FitToWorkArea()
    {
        var work = SystemParameters.WorkArea;
        MaxWidth = work.Width;
        MaxHeight = work.Height;
        Width = Math.Min(Math.Max(MinWidth, work.Width - 24), work.Width);
        Height = Math.Min(Math.Max(MinHeight, work.Height - 24), work.Height);
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + (work.Height - Height) / 2;
    }

    private void MonitorPointsScrollLeft(object sender, RoutedEventArgs e)
        => MonitorPointsSlider.ScrollToHorizontalOffset(Math.Max(0, MonitorPointsSlider.HorizontalOffset - 112));

    private void MonitorPointsScrollRight(object sender, RoutedEventArgs e)
        => MonitorPointsSlider.ScrollToHorizontalOffset(MonitorPointsSlider.HorizontalOffset + 112);

    private void MonitorPointsSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        MonitorPointsSlider.ScrollToHorizontalOffset(MonitorPointsSlider.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}
