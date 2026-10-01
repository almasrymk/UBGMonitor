using System.ComponentModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MonitorAgent.Desktop.Services;
using MonitorAgent.Desktop.Views;
using MonitorAgent.UI.ViewModels;

namespace MonitorAgent.Desktop;

public partial class MainWindow : Window
{
    private const double PinnedNavWidth = 92;
    private const double StripSize = 16;

    private static readonly string PanelsPath = AppPaths.File("panels.json");

    private sealed record PanelPins(
        bool NavPinned = true,
        bool StatusPinned = true,
        bool? DefaultNavPinned = null,
        bool? DefaultStatusPinned = null);

    private readonly AutoHidePanel _nav;
    private readonly AutoHidePanel _status;
    private readonly MainViewModel _viewModel;
    private PanelPins _pins = new();

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        _viewModel.Settings.ConfirmSaveChanges = async section => await ThemedDialog.ShowAsync(
                this,
                "Unsaved changes",
                $"You have unsaved changes in \"{section}\".\n\nDo you want to save them before leaving this tab?",
                DialogKind.Question,
                [
                    new DialogButton("Save", DialogResult.Yes, IsPrimary: true),
                    new DialogButton("Don't save", DialogResult.No),
                    new DialogButton("Cancel", DialogResult.Cancel)
                ]) switch
            {
                DialogResult.Yes => true,
                DialogResult.No => false,
                _ => null
            };
        _viewModel.Settings.ConfirmWarning = async message => await ThemedDialog.ShowAsync(
                this,
                "Warning",
                message,
                DialogKind.Warning,
                [
                    new DialogButton("Continue", DialogResult.Yes),
                    new DialogButton("Cancel", DialogResult.Cancel, IsPrimary: true)
                ]) == DialogResult.Yes;
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateStatusColumns();

        ConfigureChrome();
        this.GetObservable(WindowStateProperty).Subscribe(new Observer<WindowState>(state =>
            MaximizeButton.Content = state == WindowState.Maximized ? "❐" : "☐"));
        Opened += (_, _) => FitToWorkArea();

        _nav = new AutoHidePanel(NavPanel, NavStrip, (TranslateTransform)NavPanel.RenderTransform!, horizontal: true, pinned =>
        {
            Root.ColumnDefinitions[0].Width = new GridLength(pinned ? PinnedNavWidth : StripSize);
            Grid.SetColumnSpan(NavPanel, pinned ? 1 : 2);
        }, this);
        _status = new AutoHidePanel(StatusPanel, StatusStrip, (TranslateTransform)StatusPanel.RenderTransform!, horizontal: false, pinned =>
        {
            ContentHost.RowDefinitions[0].Height = pinned ? GridLength.Auto : new GridLength(StripSize);
            Grid.SetRowSpan(StatusPanel, pinned ? 1 : 2);
        }, this);

        _pins = LoadPins();
        ApplyPins(_pins.NavPinned, _pins.StatusPinned);

        StatusPin.Click += (_, _) => { _status.SetPinned(StatusPin.IsChecked == true); SavePins(); };
        NavPanel.AddHandler(PointerReleasedEvent, (_, _) => Dispatcher.UIThread.Post(_nav.Close), RoutingStrategies.Tunnel);

        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsXButton1Pressed && GoBack())
            {
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt && GoBack())
            {
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Windows and Linux (Wayland/compositors that allow it) draw the app's own title bar; macOS keeps its traffic
    /// lights over it; where the window cannot be extended (e.g. plain X11) the system title bar stays and the
    /// app's own window buttons are hidden.
    /// </summary>
    private void ConfigureChrome()
    {
        if (OperatingSystem.IsMacOS())
        {
            ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.PreferSystemChrome;
            CaptionButtons.IsVisible = false;
            TitleLeft.Margin = new Thickness(70, 0, 0, 0);
        }

        void Update() => CaptionButtons.IsVisible = !OperatingSystem.IsMacOS() && IsExtendedIntoWindowDecorations;
        this.GetObservable(IsExtendedIntoWindowDecorationsProperty).Subscribe(new Observer<bool>(_ => Update()));
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.HasMonitorPoints) or nameof(MainViewModel.ShowNotifications))
        {
            UpdateStatusColumns();
        }
    }

    private void UpdateStatusColumns()
    {
        var columns = StatusGrid.ColumnDefinitions;
        columns[1].Width = _viewModel.HasMonitorPoints ? GridLength.Auto : new GridLength(0);
        columns[3].Width = _viewModel.ShowNotifications ? GridLength.Auto : new GridLength(0);
        columns[4].Width = _viewModel.ShowNotifications ? new GridLength(0.75, GridUnitType.Star) : new GridLength(0);
    }

    private bool GoBack()
    {
        if (!_viewModel.GoBackCommand.CanExecute(null))
        {
            return false;
        }

        _viewModel.GoBackCommand.Execute(null);
        return true;
    }

    private void ApplyPins(bool navPinned, bool statusPinned)
    {
        StatusPin.IsChecked = statusPinned;
        _nav.SetPinned(navPinned);
        _status.SetPinned(statusPinned);
    }

    private void ToggleMenu_Click(object? sender, RoutedEventArgs e)
    {
        _nav.SetPinned(!_nav.Pinned);
        SavePins();
    }

    private void ResetLayout_Click(object? sender, RoutedEventArgs e)
    {
        Dashboard.ResetLayout(useMyDefault: true);
        ApplyPins(_pins.DefaultNavPinned ?? true, _pins.DefaultStatusPinned ?? true);
        SavePins();
    }

    private void ResetOriginalLayout_Click(object? sender, RoutedEventArgs e)
    {
        Dashboard.ResetLayout(useMyDefault: false);
        ApplyPins(true, true);
        SavePins();
    }

    private async void SaveDefaultLayout_Click(object? sender, RoutedEventArgs e)
    {
        Dashboard.SaveCurrentAsMyDefault();
        _pins = _pins with { DefaultNavPinned = _nav.Pinned, DefaultStatusPinned = _status.Pinned };
        SavePins();
        await ThemedDialog.ShowAsync(this, "Reset layout", "The current layout is saved as your default. \"Reset layout\" will return to it.",
            DialogKind.Info, [new DialogButton("OK", DialogResult.OK, IsPrimary: true)], DialogResult.OK);
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
            Directory.CreateDirectory(AppPaths.Folder);
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
        if (Screens.ScreenFromWindow(this) is not { } screen)
        {
            return;
        }

        var scale = screen.Scaling;
        var work = screen.WorkingArea;
        var width = work.Width / scale;
        var height = work.Height / scale;
        Width = Math.Min(Math.Max(MinWidth, width - 24), width);
        Height = Math.Min(Math.Max(MinHeight, height - 24), height);
        Position = new PixelPoint(
            work.X + (int)((width - Width) / 2 * scale),
            work.Y + (int)((height - Height) / 2 * scale));
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.ClickCount == 1)
        {
            BeginMoveDrag(e);
        }
    }

    private void TitleBar_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control source && source.FindAncestorOfType<Button>(includeSelf: true) is null)
        {
            Maximize_Click(sender, e);
        }
    }

    private void MonitorPointsScrollLeft(object? sender, RoutedEventArgs e)
        => MonitorPointsSlider.Offset = MonitorPointsSlider.Offset.WithX(Math.Max(0, MonitorPointsSlider.Offset.X - 112));

    private void MonitorPointsScrollRight(object? sender, RoutedEventArgs e)
        => MonitorPointsSlider.Offset = MonitorPointsSlider.Offset.WithX(MonitorPointsSlider.Offset.X + 112);

    private void MonitorPointsSlider_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var delta = Math.Abs(e.Delta.Y) >= Math.Abs(e.Delta.X) ? e.Delta.Y : e.Delta.X;
        MonitorPointsSlider.Offset = MonitorPointsSlider.Offset.WithX(Math.Max(0, MonitorPointsSlider.Offset.X - delta * 112));
        e.Handled = true;
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object? sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        (_viewModel as IDisposable)?.Dispose();
        base.OnClosed(e);
    }

    private sealed class Observer<T>(Action<T> next) : IObserver<T>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value) => next(value);
    }
}
