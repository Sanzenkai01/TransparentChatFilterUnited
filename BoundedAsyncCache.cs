using System.Collections.Concurrent;

namespace TransparentChatFilter;

internal sealed class BoundedAsyncCache<TKey, TValue> where TKey : notnull
{
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<TKey, CacheEntry> _entries;
    private int _capacity;
    private readonly TimeSpan? _successCacheDuration;
    private long _accessSequence;

    public BoundedAsyncCache(int capacity, IEqualityComparer<TKey>? comparer = null, TimeSpan? successCacheDuration = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _entries = new ConcurrentDictionary<TKey, CacheEntry>(comparer ?? EqualityComparer<TKey>.Default);
        _successCacheDuration = successCacheDuration;
    }

    internal int Count => _entries.Count;

    public void SetCapacity(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Volatile.Write(ref _capacity, capacity);
        TrimLeastRecentlyUsedEntries();
    }

    public async Task<TValue> GetOrCreateAsync(
        TKey key,
        Func<Task<TValue>> valueFactory,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                if (existing.IsExpired(DateTimeOffset.UtcNow))
                {
                    RemoveIfCurrent(key, existing);
                    continue;
                }

                existing.Touch(Interlocked.Increment(ref _accessSequence));
                return await existing.GetValueAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            var candidate = new CacheEntry(valueFactory, _successCacheDuration);
            candidate.Touch(Interlocked.Increment(ref _accessSequence));
            if (!_entries.TryAdd(key, candidate))
            {
                continue;
            }

            TrimLeastRecentlyUsedEntries();
            return await candidate.GetValueAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void TrimLeastRecentlyUsedEntries()
    {
        var excessCount = _entries.Count - Volatile.Read(ref _capacity);
        if (excessCount <= 0)
        {
            return;
        }

        foreach (var entry in _entries.OrderBy(pair => pair.Value.LastAccess).Take(excessCount))
        {
            RemoveIfCurrent(entry.Key, entry.Value);
        }
    }

    private void RemoveIfCurrent(TKey key, CacheEntry entry)
    {
        ((ICollection<KeyValuePair<TKey, CacheEntry>>)_entries).Remove(new KeyValuePair<TKey, CacheEntry>(key, entry));
    }

    private sealed class CacheEntry
    {
        private readonly Func<Task<TValue>> _valueFactory;
        private readonly TimeSpan? _successCacheDuration;
        private readonly Lazy<Task<TValue>> _valueTask;
        private long _lastAccess;
        private long _completedAtUtcTicks;
        private bool _failed;

        public CacheEntry(Func<Task<TValue>> valueFactory, TimeSpan? successCacheDuration)
        {
            _valueFactory = valueFactory;
            _successCacheDuration = successCacheDuration;
            _valueTask = new Lazy<Task<TValue>>(
                CreateAndTrackAsync,
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public long LastAccess => Volatile.Read(ref _lastAccess);

        public void Touch(long accessSequence) => Volatile.Write(ref _lastAccess, accessSequence);

        public bool IsExpired(DateTimeOffset now)
        {
            var completedAtTicks = Volatile.Read(ref _completedAtUtcTicks);
            if (completedAtTicks == 0)
            {
                return false;
            }

            var expiresAfter = Volatile.Read(ref _failed)
                ? FailureCacheDuration
                : _successCacheDuration;
            return expiresAfter.HasValue &&
                   now - new DateTimeOffset(completedAtTicks, TimeSpan.Zero) >= expiresAfter.Value;
        }

        public Task<TValue> GetValueAsync() => _valueTask.Value;

        private async Task<TValue> CreateAndTrackAsync()
        {
            try
            {
                return await _valueFactory().ConfigureAwait(false);
            }
            catch
            {
                Volatile.Write(ref _failed, true);
                throw;
            }
            finally
            {
                Volatile.Write(ref _completedAtUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
            }
        }
    }
}
