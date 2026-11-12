namespace P5RArchipelago;

internal sealed partial class ArchipelagoMod
{
    private const int PendingItemsPageSize = 6;
    private int _pendingItemsOffset;
    private long[] _pendingItemsPage = [];
    private string[] _pendingItemsNames = [];
    private string _pendingItemsStatus = "";
    private string _pendingItemsNotice = "";

    private string? GetPendingItemsMenuText(ushort item)
    {
        if (item == 0x4FC6) return _pendingItemsStatus;
        if (item is >= 0x4FC0 and < 0x4FC6)
        {
            var row = item - 0x4FC0;
            return row < _pendingItemsNames.Length ? _pendingItemsNames[row] : "--";
        }
        return null;
    }

    private int? PendingItemsMenuFeature(uint id)
    {
        if (id == 900210)
        {
            var pending = _apClient.PendingTvBundleItemIds;
            _pendingItemsOffset = Math.Clamp(_pendingItemsOffset, 0,
                Math.Max(0, (pending.Length - 1) / PendingItemsPageSize * PendingItemsPageSize));
            _pendingItemsPage = pending.Skip(_pendingItemsOffset).Take(PendingItemsPageSize).ToArray();
            _pendingItemsNames = _pendingItemsPage.Select(item =>
                _tvBundles.TryGet(item, out var reward) ? reward.Name : $"Unknown reward 0x{item:X}").ToArray();
            _pendingItemsStatus = pending.Length == 0 ? "No pending inventory items." :
                $"{pending.Length} pending - page {_pendingItemsOffset / PendingItemsPageSize + 1}/{(pending.Length + PendingItemsPageSize - 1) / PendingItemsPageSize}";
            if (_pendingItemsNotice.Length > 0) _pendingItemsStatus += " - " + _pendingItemsNotice;
            return pending.Length;
        }
        if (id == 900211) { _pendingItemsOffset += PendingItemsPageSize; return 1; }
        if (id == 900212) { _pendingItemsOffset = Math.Max(0, _pendingItemsOffset - PendingItemsPageSize); return 1; }
        if (id == 900213) { _pendingItemsOffset = 0; _pendingItemsNotice = ""; return 1; }
        if (id is not (900214 or 900215 or 900216) && id is not (>= 900220 and < 900226)) return null;
        if (!_loadedSaveSlot.HasValue || Volatile.Read(ref _hubMenuOpen) == 0 ||
            Volatile.Read(ref _connectionBusy) != 0) return 0;
        if (id == 900215)
        {
            if (!_apClient.ConnectionReady)
            {
                _pendingItemsNotice = "Connect before applying party levels";
                return 0;
            }
            if (_automaticRewards?.Executing == true)
            {
                _pendingItemsNotice = "Rewards are already being applied";
                return 0;
            }
            // The BF closes its selector and ends its field event before calling
            // this guard, then invokes the normal resumable party-level helper.
            // Credits are acknowledged only by that helper's native level readback.
            _pendingItemsNotice = "";
            _log.Info("Pending Items: manually applying earned party level rewards.");
            return 1;
        }
        if (id == 900216)
        {
            _pendingItemsNotice = "Party levels checked";
            _log.Info("Pending Items: manual party level reconciliation finished.");
            return 1;
        }
        if (id == 900214)
        {
            var claimed = _inventoryRewards.Apply(int.MaxValue);
            _pendingItemsNotice = claimed > 0 ? $"Claimed {claimed}" :
                _apClient.HasPendingInventoryRewards ? "Delivery deferred; try again" : "";
            _log.Info($"Pending Items: claimed {claimed} reward(s); {_apClient.PendingTvBundleItemIds.Length} remain.");
            return claimed;
        }
        var row = (int)(id - 900220);
        if (row >= _pendingItemsPage.Length ||
            !_apClient.PrioritizePendingInventoryReward(_pendingItemsOffset + row, _pendingItemsPage[row])) return 0;
        var result = _inventoryRewards.Apply(int.MaxValue, maximumRewards: 1);
        _pendingItemsNotice = result > 0 ? "Claimed 1" : "Delivery deferred; try again";
        _log.Info($"Pending Items: claimed {result} selected reward(s).");
        return result;
    }
}
