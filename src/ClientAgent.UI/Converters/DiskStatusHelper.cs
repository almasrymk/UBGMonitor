namespace ClientAgent.UI.Converters;

public static class DiskStatusHelper
{
    public static string FromUsage(double percent)
        => percent >= 90 ? "Critical" : percent >= 75 ? "Warning" : "Healthy";
}
