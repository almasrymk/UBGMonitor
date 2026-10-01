using Avalonia.Threading;

namespace MonitorAgent.UI.Services;

/// <summary>A timer that ticks on the UI thread.</summary>
public sealed class UiTimer
{
    private readonly DispatcherTimer _timer;

    public UiTimer(TimeSpan interval)
    {
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += (_, e) => Tick?.Invoke(this, e);
    }

    public event EventHandler? Tick;

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public bool IsEnabled => _timer.IsEnabled;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();
}
