namespace MonitorAgent.UI.Services;

public static class SensitiveClipboard
{
    public static async Task CopyAsync(string value, Func<string, Task> set, Func<Task<string?>> get, Func<Task> clear, Func<Task>? delay = null)
    {
        await set(value);
        await (delay?.Invoke() ?? Task.Delay(TimeSpan.FromSeconds(30)));
        if (string.Equals(await get(), value, StringComparison.Ordinal)) await clear();
    }
}
