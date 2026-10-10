namespace MonitorAgent.UI.Services;

/// <summary>Does not keep images alive after their displayed rows release them.</summary>
public sealed class BoundedWeakCache<T> where T : class
{
    private readonly int _capacity;
    private readonly Dictionary<string, WeakReference<T>> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _recent = new();
    private readonly object _gate = new();

    public BoundedWeakCache(int capacity) => _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    public int Count { get { lock (_gate) return _items.Count; } }
    public T? Get(string key, Func<string, T?> load)
    {
        var sourceKey = key;
        key = key.ToUpperInvariant();
        lock (_gate)
        {
            if (_items.TryGetValue(key, out var reference) && reference.TryGetTarget(out var found))
            {
                _recent.Remove(key);
                _recent.AddLast(key);
                return found;
            }
            var value = load(sourceKey);
            _recent.Remove(key);
            _items.Remove(key);
            if (value is null) return null;
            while (_items.Count >= _capacity)
            {
                _items.Remove(_recent.First!.Value);
                _recent.RemoveFirst();
            }
            _items[key] = new WeakReference<T>(value);
            _recent.AddLast(key);
            return value;
        }
    }
}
