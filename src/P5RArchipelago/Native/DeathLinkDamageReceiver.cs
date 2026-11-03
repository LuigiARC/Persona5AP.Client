namespace P5RArchipelago.Native;

/// <summary>Consumes damage only from P5R's own HP-reader callback. No polling thread,
/// BF launch, scene change, or flow interpreter is involved.</summary>
internal sealed class DeathLinkDamageReceiver : IDisposable
{
    private readonly Func<int> _pendingCount;
    private readonly Func<bool> _mayApply;
    private readonly Func<int> _consumePercent;
    private readonly Func<int, int> _apply;
    private readonly Action _requeue;
    private readonly Log _log;
    private int _applying;
    private int _disposed;

    public DeathLinkDamageReceiver(Func<int> pendingCount, Func<bool> mayApply,
        Func<int> consumePercent, Func<int, int> apply, Action requeue, Log log)
    {
        _pendingCount = pendingCount;
        _mayApply = mayApply;
        _consumePercent = consumePercent;
        _apply = apply;
        _requeue = requeue;
        _log = log;
    }

    public void OnGameplayHpRead()
    {
        if (Volatile.Read(ref _disposed) != 0 || _pendingCount() <= 0 || !_mayApply() ||
            Interlocked.CompareExchange(ref _applying, 1, 0) != 0)
            return;
        try
        {
            var percent = _consumePercent();
            if (percent <= 0) return;
            try
            {
                var changed = _apply(percent);
                _log.Info($"Applied queued DeathLink on native HP read as {percent}% max-HP damage to {changed} active party member(s) ({_pendingCount()} pending).");
            }
            catch (Exception ex)
            {
                _requeue();
                _log.Error($"DeathLink HP validation failed and was requeued: {ex.Message}");
            }
        }
        finally { Volatile.Write(ref _applying, 0); }
    }

    internal static int TargetHp(int currentHp, int maximumHp, int percent)
    {
        if (currentHp <= 0 || percent <= 0) return currentHp;
        var damage = Math.Max(1, maximumHp * Math.Clamp(percent, 1, 100) / 100);
        return Math.Max(1, currentHp - damage);
    }

    public void Dispose() => Volatile.Write(ref _disposed, 1);
}
