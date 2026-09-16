using System.Windows;
using System.Windows.Input;
using ClientAgent.UI.ViewModels;

namespace ClientAgent.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        StateChanged += (_, _) => MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "☐";
        Loaded += (_, _) => FitToWorkArea();
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
