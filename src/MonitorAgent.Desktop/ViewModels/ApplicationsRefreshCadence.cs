namespace MonitorAgent.UI.ViewModels;

public static class ApplicationsRefreshCadence
{
    public static int Seconds(bool visible, int configuredSeconds)
        => visible ? Math.Max(1, configuredSeconds) : Math.Max(30, configuredSeconds);
}
