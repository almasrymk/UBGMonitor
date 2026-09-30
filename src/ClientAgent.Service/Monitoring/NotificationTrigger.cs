namespace ClientAgent.Service.Monitoring;

/// <summary>Wakes the notification engine as soon as a check result changes, instead of waiting for its next round.</summary>
public sealed class NotificationTrigger
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Request()
    {
        try
        {
            if (_signal.CurrentCount == 0)
            {
                _signal.Release();
            }
        }
        catch (SemaphoreFullException)
        {
        }
    }

    /// <returns>True when a change was requested, false when the timeout passed first.</returns>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);
}
