namespace Lingarr.Server.Providers;

public sealed class LogPage
{
    public required IReadOnlyList<LogEntry> Items { get; init; }

    public bool HasOlder { get; init; }

    public long? OldestId { get; init; }

    public long? NewestId { get; init; }

    public long LatestId { get; init; }
}

public sealed class LogBuffer
{
    private readonly object _gate = new();
    private readonly List<LogEntry> _logs = [];
    private readonly int _maxCount;
    private readonly TimeSpan _maxAge;
    private long _latestId;
    private event Action<LogEntry>? _arrived;

    public LogBuffer(int maxCount = 1000, TimeSpan? maxAge = null)
    {
        _maxCount = maxCount;
        _maxAge = maxAge ?? TimeSpan.FromHours(24);
    }

    public long LatestId
    {
        get
        {
            lock (_gate)
            {
                return _latestId;
            }
        }
    }

    public LogEntry Add(LogEntry entry)
    {
        lock (_gate)
        {
            entry.Id = ++_latestId;
            if (entry.Timestamp == default)
            {
                entry.Timestamp = DateTime.UtcNow;
            }

            entry.Hint = LogHints.For(entry.LogLevel, entry.Message, entry.ExceptionText);
            _logs.Add(entry);
            Trim();
        }

        Publish(entry);
        return entry;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _logs.Clear();
        }
    }

    public LogPage Page(long? before, long? after, int limit, LogLevel? minimum)
    {
        lock (_gate)
        {
            var filtered = Filtered(minimum);
            List<LogEntry> items;
            if (after != null)
            {
                items = filtered.Where(entry => entry.Id > after.Value).Take(limit).ToList();
            }
            else if (before != null)
            {
                items = filtered.Where(entry => entry.Id < before.Value).TakeLast(limit).ToList();
            }
            else
            {
                items = filtered.TakeLast(limit).ToList();
            }

            long? oldest = items.Count == 0 ? null : items[0].Id;
            long? newest = items.Count == 0 ? null : items[^1].Id;
            var hasOlder = oldest != null && filtered.Count > 0 && filtered[0].Id < oldest.Value;
            return new LogPage
            {
                Items = items,
                HasOlder = hasOlder,
                OldestId = oldest,
                NewestId = newest,
                LatestId = _latestId
            };
        }
    }

    public async Task<IReadOnlyList<LogEntry>> WaitAsync(
        long after,
        LogLevel? minimum,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var ready = Page(before: null, after: after, limit: 100, minimum).Items;
        if (ready.Count > 0)
        {
            return ready;
        }

        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnArrived(LogEntry entry)
        {
            if (entry.Id > after && (minimum == null || entry.LogLevel >= minimum))
            {
                pending.TrySetResult(true);
            }
        }

        _arrived += OnArrived;
        try
        {
            ready = Page(before: null, after: after, limit: 100, minimum).Items;
            if (ready.Count > 0)
            {
                return ready;
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            using var _ = timeoutSource.Token.Register(() => pending.TrySetResult(false));
            await pending.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return Page(before: null, after: after, limit: 100, minimum).Items;
        }
        finally
        {
            _arrived -= OnArrived;
        }
    }

    private List<LogEntry> Filtered(LogLevel? minimum)
    {
        if (minimum == null)
        {
            return _logs.ToList();
        }

        return _logs.Where(entry => entry.LogLevel >= minimum).ToList();
    }

    private void Trim()
    {
        while (_logs.Count > _maxCount)
        {
            _logs.RemoveAt(0);
        }

        var cutoff = DateTime.UtcNow - _maxAge;
        while (_logs.Count > 0 && _logs[0].Timestamp < cutoff)
        {
            _logs.RemoveAt(0);
        }
    }

    private void Publish(LogEntry entry)
    {
        var handler = _arrived;
        if (handler == null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action<LogEntry>)subscriber)(entry);
            }
            catch (Exception)
            {
                // A follower must not stop the log from being stored.
            }
        }
    }
}
