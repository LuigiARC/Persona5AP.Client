namespace P5RArchipelago;

/// <summary>Bounded, reentry-safe delivery with a durable cursor for partial multi-item bundles.</summary>
internal sealed class InventoryRewardReceiver
{
    private readonly TvBundleCatalog _catalog;
    private readonly Func<(long ItemId, int ContentIndex)?> _peek;
    private readonly Action<long, int> _advance;
    private readonly Action<long> _complete;
    private readonly Func<ushort, byte, bool> _grant;
    private readonly Func<long> _now;
    private readonly Log _log;
    private int _applying;
    private long _retryAfter;

    public InventoryRewardReceiver(TvBundleCatalog catalog, Func<(long, int)?> peek,
        Action<long, int> advance, Action<long> complete, Func<ushort, byte, bool> grant,
        Log log, Func<long>? now = null)
    {
        _catalog = catalog; _peek = peek; _advance = advance; _complete = complete;
        _grant = grant; _log = log; _now = now ?? (() => Environment.TickCount64);
    }

    public bool CanApply => _now() >= Volatile.Read(ref _retryAfter);

    public int Apply(int maximumContents = 4, int maximumRewards = int.MaxValue)
    {
        if (!CanApply || Interlocked.CompareExchange(ref _applying, 1, 0) != 0) return 0;
        var completed = 0;
        try
        {
            var granted = 0;
            while (granted < maximumContents && completed < maximumRewards && _peek() is { } pending)
            {
                if (!_catalog.TryGet(pending.ItemId, out var reward) ||
                    pending.ContentIndex < 0 || pending.ContentIndex > reward.Contents.Count)
                    throw new InvalidDataException($"Unsupported pending inventory reward 0x{pending.ItemId:X}.");
                if (pending.ContentIndex == reward.Contents.Count)
                {
                    _complete(pending.ItemId);
                    completed++;
                    _log.Info($"Applied AP inventory reward '{reward.Name}'.");
                    continue;
                }
                var content = reward.Contents[pending.ContentIndex];
                _log.Info($"[RewardDebug] Granting '{reward.Name}' content {pending.ContentIndex + 1}/{reward.Contents.Count}: native 0x{content.ItemId:X4} x{content.Count}.");
                if (!_grant(content.ItemId, content.Count))
                    throw new InvalidOperationException($"Native grant readback failed for 0x{content.ItemId:X4}; reward retained.");
                _advance(pending.ItemId, pending.ContentIndex);
                granted++;
                // Finish the receipt even if this was the final content in this update's budget.
                if (pending.ContentIndex + 1 == reward.Contents.Count)
                {
                    _complete(pending.ItemId);
                    completed++;
                    _log.Info($"Applied AP inventory reward '{reward.Name}'.");
                }
            }
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _retryAfter, _now() + 2000);
            _log.Error($"AP inventory delivery deferred: {ex.Message}");
        }
        finally { Volatile.Write(ref _applying, 0); }
        return completed;
    }
}
