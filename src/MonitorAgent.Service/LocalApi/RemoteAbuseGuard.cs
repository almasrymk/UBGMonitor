namespace MonitorAgent.Service.LocalApi;
public sealed class RemoteAbuseGuard
{
    private readonly Dictionary<string, (int Count, DateTime Until)> _failures = new();
    private readonly object _gate = new();
    public bool IsBlocked(string peer)
    {
        lock (_gate)
        {
            foreach (var expired in _failures.Where(p => p.Value.Until < DateTime.UtcNow).Select(p => p.Key).ToList()) _failures.Remove(expired);
            return _failures.TryGetValue(peer, out var failed) && failed.Count >= 5 || _failures.Count >= 2048 && !_failures.ContainsKey(peer);
        }
    }
    public void Failed(string peer)
    {
        lock (_gate)
        {
            if (_failures.TryGetValue(peer, out var old)) _failures[peer] = (old.Count + 1, old.Until);
            else if (_failures.Count < 2048) _failures[peer] = (1, DateTime.UtcNow.AddMinutes(1));
        }
    }
}
