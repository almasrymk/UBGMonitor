namespace ClientAgent.UI.Services;

public sealed class ProcessQueryNotSupportedException : Exception
{
    public ProcessQueryNotSupportedException()
        : base("Network per-process not available")
    {
    }
}
