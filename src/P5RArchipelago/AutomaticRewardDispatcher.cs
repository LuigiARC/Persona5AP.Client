namespace P5RArchipelago;

internal readonly record struct RewardStamp(object Source, long SaveGeneration, long Version);

/// <summary>Enqueues one silent game-flow job from native gameplay input, never from the socket thread.</summary>
internal sealed class AutomaticRewardDispatcher : IDisposable
{
    private readonly Func<RewardStamp?> _stamp;
    private readonly Func<bool> _mayApply;
    private readonly Func<bool> _hasPending;
    private readonly Action _enqueue;
    private readonly Func<long> _now;
    private readonly Log _log;
    private readonly object _gate = new();
    private RewardStamp? _queued, _executing, _completed;
    private long _queuedAt, _executingAt, _retryAfter, _completedAt;
    private bool _disposed;
    private bool _traceJob;

    public string Diagnostic
    {
        get
        {
            lock (_gate)
            {
                var now = _now();
                return $"queued={_queued.HasValue}, executing={_executing.HasValue}, " +
                    $"queueAgeMs={(_queued.HasValue ? now - _queuedAt : 0)}, " +
                    $"executeAgeMs={(_executing.HasValue ? now - _executingAt : 0)}, " +
                    $"retryMs={Math.Max(0, _retryAfter - now)}";
            }
        }
    }

    public AutomaticRewardDispatcher(Func<RewardStamp?> stamp, Func<bool> mayApply,
        Func<bool> hasPending, Action enqueue, Log log, Func<long>? now = null)
    {
        _stamp = stamp; _mayApply = mayApply; _hasPending = hasPending;
        _enqueue = enqueue; _log = log; _now = now ?? (() => Environment.TickCount64);
    }

    public bool Busy
    {
        get
        {
            lock (_gate)
            {
                ExpireCanceledJobs();
                return _queued.HasValue || _executing.HasValue;
            }
        }
    }
    public bool Executing { get { lock (_gate) return _executing.HasValue; } }

    public void Poll()
    {
        lock (_gate)
        {
            ExpireCanceledJobs();
            if (_disposed || _executing.HasValue || !_mayApply() || _stamp() is not { } stamp) return;
            var now = _now();
            if (now < _retryAfter) return;
            if (_queued is { } queued && SameSession(queued, stamp) && now - _queuedAt < 2000) return;
            _queued = null;
            // Quietly revisit party records that may have initialized after recruitment.
            if (_completed == stamp && !_hasPending() && now - _completedAt < 5000) return;
            _queued = stamp;
            _queuedAt = now;
            _traceJob = _hasPending();
            if (_traceJob) _log.Info($"[RewardDebug] Queuing automatic flow procedure 34 (saveGeneration={stamp.SaveGeneration}, revision={stamp.Version}).");
            try { _enqueue(); }
            catch (Exception ex)
            {
                _queued = null;
                _retryAfter = now + 2000;
                _log.Error($"Automatic AP reward dispatch deferred: {ex.Message}");
            }
        }
    }

    public bool TryBegin()
    {
        lock (_gate)
        {
            if (_disposed || _executing.HasValue || _queued is not { } queued) return false;
            _queued = null;
            if (!_mayApply() || _stamp() is not { } stamp || !SameSession(queued, stamp))
            {
                if (_traceJob) _log.Info("[RewardDebug] Flow procedure 34 reached begin callback, but safety/session check rejected it.");
                return false;
            }
            _executing = queued;
            _executingAt = _now();
            if (_traceJob) _log.Info("[RewardDebug] Flow procedure 34 began; inventory callback 900130 should follow.");
            return true;
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            if (_executing is not { } executing) return;
            if (_traceJob) _log.Info("[RewardDebug] Automatic flow completed callback 900201.");
            _completed = executing;
            _completedAt = _now();
            // Input is queried several times per frame. Spread backlog jobs over updates.
            _retryAfter = Math.Max(_retryAfter, _completedAt + 16);
            _executing = null;
        }
    }

    private static bool SameSession(RewardStamp a, RewardStamp b) =>
        ReferenceEquals(a.Source, b.Source) && a.SaveGeneration == b.SaveGeneration;

    private void ExpireCanceledJobs()
    {
        var now = _now();
        if (_queued.HasValue && now - _queuedAt >= 2000)
        {
            if (_traceJob) _log.Info("[RewardDebug] Queued flow expired without begin callback 900200; check native enqueue/script execution.");
            _queued = null;
        }
        if (_executing.HasValue && now - _executingAt >= 10000)
        {
            _executing = null;
            _retryAfter = now + 2000;
            _log.Error("Automatic AP reward flow did not complete; retry deferred and F9 remains available.");
        }
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _queued = _executing = null; }
    }
}
