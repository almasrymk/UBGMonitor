namespace MonitorAgent.Desktop;

/// <summary>The app's per-user folder (layout, panel pins, key).</summary>
internal static class AppPaths
{
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorAgent");

    public static string File(string name) => Path.Combine(Folder, name);
}
