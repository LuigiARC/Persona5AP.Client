using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X86;
using System.Runtime.InteropServices;
using System.Text;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

internal sealed unsafe class ChestMonitor : IDisposable
{
    private const string StartOpenChestPattern =
        "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 56 41 57 48 83 EC 60 48 8B FA 4C 8B F9";

    private const string CompleteOpenChestPattern =
        "40 53 48 83 EC 20 48 8B 59 48 48 8B 4B 20 48 85 C9 74 ?? E8 ?? ?? ?? ?? 48 8B 4B 30 48 85 C9 74 ?? E8 ?? ?? ?? ?? 48 8B CB 48 83 C4 20 5B E9 ?? ?? ?? ??";

    // Exact Steam P5R build signatures. The generic prologues form a large family; retaining
    // the build-specific RIP-relative displacement makes these unique and fail-closed.
    private const string GetItemNamePattern =
        "4C 8B DC 48 83 EC 78 48 8D 05 42 2E 01 00 BA FF 0F 00 00";
    private const string GetItemNumPattern =
        "4C 8B DC 48 83 EC 78 48 8D 05 D2 F3 FF FF BA FF 0F 00 00";
    private const string SetItemNumPattern =
        "4C 8B DC 49 89 5B ?? 57 48 83 EC 70 48 8D 05 ?? ?? ?? ??";

    private const string GetItemPricePattern =
        "4C 8B DC 48 83 EC 78 48 8D 05 92 E5 FF FF 49 C7 43 C8 00 00 00 00";

    private const ushort ConfidantShopMarkerFirst = 0x0011;
    private const ushort ConfidantShopReturnMarker = 0x0028;
    private static readonly ushort[] ConfidantShopCmmIds =
        [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 33, 35];

    [Function(CallingConventions.Fastcall)]
    private delegate IntPtr StartOpenChest(IntPtr first, long second, ushort third);

    [Function(CallingConventions.Fastcall)]
    private delegate void CompleteOpenChest(long first, IntPtr second, long third, int fourth);

    [Function(CallingConventions.Fastcall)]
    private delegate byte* GetItemName(ushort itemId);

    [Function(CallingConventions.Fastcall)]
    private delegate byte GetItemNum(ushort itemId);

    [Function(CallingConventions.Fastcall)]
    private delegate void SetItemNum(ushort itemId, byte newCount, byte shouldUpdateRecent);

    [Function(CallingConventions.Fastcall)]
    private delegate int GetItemPrice(ushort itemId);

    private readonly IHook<StartOpenChest> _startHook;
    private readonly IHook<CompleteOpenChest> _completeHook;
    private readonly IHook<GetItemName> _getItemNameHook;
    private readonly IHook<GetItemNum> _getItemNumHook;
    private readonly IHook<SetItemNum> _setItemNumHook;
    private readonly IHook<GetItemPrice> _getItemPriceHook;
    // Native item-help renderer, verified at public-shop calls F37007/F37530. Parameters
    // 9/10 select the help table and item; the first two are the animated panel coordinates.
    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    private delegate long DrawItemHelp(float x, float y, float unused, uint alpha,
        float unusedScale, int mode, long unused7, long unused8, int category, uint itemId);
    [Reloaded.Hooks.Definitions.X64.Function(Reloaded.Hooks.Definitions.X64.CallingConventions.Microsoft)]
    private delegate int DrawPlainText(float x, float y, float z, uint color, float scale,
        byte unused6, byte unused7, byte* text, int unused9, int unused10);
    private IHook<DrawItemHelp>? _drawItemHelpHook;
    private DrawPlainText? _drawPlainText;
    private readonly Dictionary<ushort, (string Text, List<GCHandle> Lines)> _shopDescriptionHandles = [];
    private readonly TboxFlagReader _flagReader;
    private readonly bool _logAllChestFlags;
    private readonly Log _log;
    private readonly Func<uint, string?> _rewardNameResolver;
    private readonly Func<string?> _genericRewardNameResolver;
    private readonly Func<bool> _confidantShopActive;
    private readonly Func<ushort, ConfidantShopOffer?> _confidantShopOfferResolver;
    private readonly Action<ushort> _confidantShopPurchase;
    private readonly Action _confidantShopReturnRequested;
    private uint? _activeChestFlag;
    private bool _suppressVanillaReward;
    private GCHandle _rewardNameHandle;
    private readonly Dictionary<ushort, (string Text, GCHandle Handle)> _shopNameHandles = [];
    private readonly List<GCHandle> _retiredShopNameHandles = [];

    public event Action<uint>? ChestOpenedCompleted;

    public ChestMonitor(IReloadedHooks hooks, AddressScanner scanner, TboxFlagReader flagReader,
        Func<uint, string?> rewardNameResolver, Func<string?> genericRewardNameResolver,
        Func<bool> confidantShopActive,
        Func<ushort, ConfidantShopOffer?> confidantShopOfferResolver,
        Action<ushort> confidantShopPurchase, Action confidantShopReturnRequested,
        bool logAllChestFlags, Log log)
    {
        _flagReader = flagReader;
        _rewardNameResolver = rewardNameResolver;
        _genericRewardNameResolver = genericRewardNameResolver;
        _confidantShopActive = confidantShopActive;
        _confidantShopOfferResolver = confidantShopOfferResolver;
        _confidantShopPurchase = confidantShopPurchase;
        _confidantShopReturnRequested = confidantShopReturnRequested;
        _logAllChestFlags = logAllChestFlags;
        _log = log;

        var startAddress = scanner.FindUnique("StartOpenChest", StartOpenChestPattern);
        var completeAddress = scanner.FindUnique("CompleteOpenChest", CompleteOpenChestPattern);
        _startHook = hooks.CreateHook<StartOpenChest>(StartOpenChestImpl, startAddress).Activate();
        _completeHook = hooks.CreateHook<CompleteOpenChest>(CompleteOpenChestImpl, completeAddress).Activate();
        _getItemNameHook = hooks.CreateHook<GetItemName>(GetItemNameImpl,
            scanner.FindUnique("GetItemName", GetItemNamePattern)).Activate();
        _getItemNumHook = hooks.CreateHook<GetItemNum>(GetItemNumImpl,
            scanner.FindUnique("GetItemNum", GetItemNumPattern)).Activate();
        _setItemNumHook = hooks.CreateHook<SetItemNum>(SetItemNumImpl,
            scanner.FindUnique("SetItemNum", SetItemNumPattern)).Activate();
        _getItemPriceHook = hooks.CreateHook<GetItemPrice>(GetItemPriceImpl,
            scanner.FindUnique("GetItemPrice", GetItemPricePattern)).Activate();
        // Prepare only: shared native renderer stays unpatched until the AP shop opens.
        InitializeShopDescriptions(hooks, scanner);
    }

    private void InitializeShopDescriptions(IReloadedHooks hooks, AddressScanner scanner)
    {
        try
        {
            _drawPlainText = hooks.CreateWrapper<DrawPlainText>(scanner.FindUnique("DrawPlainText",
                "48 83 EC 68 83 3D 21 85 3A 01 01 0F 28 D8 F3 0F 10 05"), out _);
            _drawItemHelpHook = hooks.CreateHook<DrawItemHelp>(DrawItemHelpImpl,
                scanner.FindUnique("DrawItemHelp",
                "48 8B C4 48 81 EC 98 00 00 00 66 0F 6F 1D EE 2A AB 00 45 8B D1"));
            _log.Info("Prepared AP shop descriptions; renderer hook remains inactive outside the AP shop.");
        }
        catch (Exception ex)
        {
            _log.Error($"AP shop description renderer disabled safely: {ex.Message}");
        }
    }

    public void SetShopDescriptionsEnabled(bool enabled)
    {
        if (_drawItemHelpHook is null) return;
        try
        {
            if (enabled)
            {
                if (_drawItemHelpHook.IsHookActivated) _drawItemHelpHook.Enable();
                else _drawItemHelpHook.Activate();
            }
            else if (_drawItemHelpHook.IsHookActivated) _drawItemHelpHook.Disable();
        }
        catch (Exception ex)
        {
            if (_drawItemHelpHook.IsHookActivated) _drawItemHelpHook.Disable();
            _log.Error($"Could not enable AP shop descriptions; vanilla help retained: {ex.Message}");
        }
    }

    private long DrawItemHelpImpl(float x, float y, float unused, uint alpha,
        float unusedScale, int mode, long unused7, long unused8, int category, uint itemId)
    {
        if (_confidantShopActive() && _drawPlainText is not null && category is 0 or 10 &&
            itemId <= ushort.MaxValue && ((ushort)itemId == ConfidantShopReturnMarker ||
                TryGetConfidantShopCmmId((ushort)itemId, out _)))
        {
            TryGetConfidantShopCmmId((ushort)itemId, out var cmmId);
            var description = (ushort)itemId == ConfidantShopReturnMarker
                ? "Leave the shop and return\nto the Mementos hub."
                : _confidantShopOfferResolver(cmmId)?.Description;
            if (!string.IsNullOrEmpty(description))
            {
                var marker = (ushort)itemId;
                if (!_shopDescriptionHandles.TryGetValue(marker, out var cached) || cached.Text != description)
                {
                    // Retire replaced buffers until the entire shop controller closes.
                    if (cached.Lines is not null) _retiredShopNameHandles.AddRange(cached.Lines);
                    var lines = WrapDescription(description).Take(7).Select(line => GCHandle.Alloc(
                        Encoding.UTF8.GetBytes(line + '\0'), GCHandleType.Pinned)).ToList();
                    cached = (description, lines);
                    _shopDescriptionHandles[marker] = cached;
                }
                for (var line = 0; line < cached.Lines.Count; line++)
                    _drawPlainText(x, y + line * 23f, 0, alpha & 0xff, 0.65f, 0, 0,
                        (byte*)cached.Lines[line].AddrOfPinnedObject(), 0, 0);
                return 0;
            }
        }
        return _drawItemHelpHook!.OriginalFunction(x, y, unused, alpha, unusedScale, mode,
            unused7, unused8, category, itemId);
    }

    private static IEnumerable<string> WrapDescription(string text)
    {
        foreach (var paragraph in text.Split('\n'))
        {
            var rest = paragraph.Replace('\r', ' ');
            while (rest.Length > 28)
            {
                var split = rest.LastIndexOf(' ', 28);
                if (split < 8) split = 28;
                yield return rest[..split];
                rest = rest[split..].TrimStart();
            }
            yield return rest;
        }
    }

    private IntPtr StartOpenChestImpl(IntPtr first, long second, ushort third)
    {
        var result = _startHook.OriginalFunction(first, second, third);
        try
        {
            _activeChestFlag = _flagReader.ReadCurrentFlag();
            var rewardName = _rewardNameResolver(_activeChestFlag.Value);
            _suppressVanillaReward = false;
            if (rewardName is null)
            {
                // Mementos's regenerating chests have no catalogued TBOX flag. The current
                // Path and its durable per-Path counter identify the AP location instead.
                rewardName = _genericRewardNameResolver();
                _suppressVanillaReward = rewardName is not null;
            }
            if (rewardName is not null)
            {
                ClearRewardName();
                _rewardNameHandle = GCHandle.Alloc(Encoding.UTF8.GetBytes(rewardName + '\0'), GCHandleType.Pinned);
            }
            if (_logAllChestFlags)
                _log.Info($"Chest opening started: tbox flag 0x{_activeChestFlag.Value:X8}.");
        }
        catch (Exception ex)
        {
            _activeChestFlag = null;
            _suppressVanillaReward = false;
            _log.Error($"Could not read the active chest flag: {ex.Message}");
        }

        return result;
    }

    private void CompleteOpenChestImpl(long first, IntPtr second, long third, int fourth)
    {
        _completeHook.OriginalFunction(first, second, third, fourth);

        var flag = _activeChestFlag;
        _activeChestFlag = null;
        ClearRewardName();
        if (flag.HasValue)
        {
            if (_logAllChestFlags)
                _log.Info($"Chest opening completed: tbox flag 0x{flag.Value:X8}.");
            ChestOpenedCompleted?.Invoke(flag.Value);
        }
    }

    private byte* GetItemNameImpl(ushort itemId)
    {
        var menuText = ConnectionMenuText?.Invoke(itemId);
        if (menuText is not null) return PinMenuText(itemId, menuText);
        if (_activeChestFlag.HasValue && _rewardNameHandle.IsAllocated)
            return (byte*)_rewardNameHandle.AddrOfPinnedObject();
        if (_confidantShopActive() && TryGetConfidantShopName(itemId, out var shopName))
            return shopName;
        return _getItemNameHook.OriginalFunction(itemId);
    }

    public Func<ushort, string?>? ConnectionMenuText { get; set; }

    private byte* PinMenuText(ushort itemId, string text)
    {
        if (_shopNameHandles.TryGetValue(itemId, out var cached) && cached.Text == text)
            return (byte*)cached.Handle.AddrOfPinnedObject();
        if (cached.Handle.IsAllocated) _retiredShopNameHandles.Add(cached.Handle);
        var handle = GCHandle.Alloc(Encoding.UTF8.GetBytes(text + '\0'), GCHandleType.Pinned);
        _shopNameHandles[itemId] = (text, handle);
        return (byte*)handle.AddrOfPinnedObject();
    }

    private byte GetItemNumImpl(ushort itemId)
    {
        if (_confidantShopActive())
        {
            // Report 98/99 so the native quantity picker permits exactly one purchase. The
            // marker is never actually stored, so the next rank can still be bought afterward.
            if (itemId == ConfidantShopReturnMarker) return 98;
            if (TryGetConfidantShopCmmId(itemId, out var cmmId))
                return _confidantShopOfferResolver(cmmId)?.TargetRank > 0 ? (byte)98 : (byte)99;
        }
        return _getItemNumHook.OriginalFunction(itemId);
    }

    private int GetItemPriceImpl(ushort itemId)
    {
        if (_confidantShopActive())
        {
            // Public-shop initialization calculates an affordability count by dividing
            // the player's money by every stock row's price, including sold-out rows.
            // Returning zero here crashes at p5r.exe+0xF35A1B (integer divide by zero).
            if (itemId == ConfidantShopReturnMarker) return 1;
            if (TryGetConfidantShopCmmId(itemId, out var cmmId))
                return Math.Max(1, _confidantShopOfferResolver(cmmId)?.Price ?? 0);
        }
        return _getItemPriceHook.OriginalFunction(itemId);
    }

    private void SetItemNumImpl(ushort itemId, byte newCount, byte shouldUpdateRecent)
    {
        if (_confidantShopActive())
        {
            if (itemId == ConfidantShopReturnMarker)
            {
                // The public-shop controller may write zero while tearing its stock down.
                // Our virtual owned count is 98, so only the real one-unit purchase writes
                // 99. Treating a cleanup write as activation silently forced a field reload,
                // cleared the AP-Palace fallback, and left subsequent F9 presses rejected.
                if (newCount == 99)
                {
                    _log.Info("Archipelago shop return-to-hub row purchased.");
                    _confidantShopReturnRequested();
                }
                return;
            }
            if (TryGetConfidantShopCmmId(itemId, out var cmmId))
            {
                if (newCount > 0)
                    _confidantShopPurchase(cmmId);
                return;
            }
        }

        // The AP DATTBOX override uses blank key item 0x4000 only to make the vanilla chest
        // animation and item window execute. Never put that marker in inventory.
        if (_activeChestFlag.HasValue && _rewardNameHandle.IsAllocated &&
            (_suppressVanillaReward || itemId == 0x4000))
            return;
        _setItemNumHook.OriginalFunction(itemId, newCount, shouldUpdateRecent);
    }

    public bool GrantItem(ushort itemId, byte count)
    {
        var current = _getItemNumHook.OriginalFunction(itemId);
        var next = (byte)Math.Min(99, current + count);
        _setItemNumHook.OriginalFunction(itemId, next, 1);
        var actual = _getItemNumHook.OriginalFunction(itemId);
        if (actual != next)
            _log.Error($"P5R item 0x{itemId:X4} grant failed: requested {next}, native readback {actual} (was {current}).");
        else
            _log.Info($"Granted P5R item 0x{itemId:X4} x{actual - current} ({current} -> {actual}; native readback verified).");
        return actual == next;
    }

    private void ClearRewardName()
    {
        if (_rewardNameHandle.IsAllocated)
            _rewardNameHandle.Free();
    }

    private bool TryGetConfidantShopName(ushort itemId, out byte* name)
    {
        string? text = null;
        if (itemId == ConfidantShopReturnMarker)
            text = "Return to Mementos Hub (1 yen)";
        else if (TryGetConfidantShopCmmId(itemId, out var cmmId))
            text = _confidantShopOfferResolver(cmmId)?.DisplayName ?? "Confidant - Complete";

        if (text is null)
        {
            name = null;
            return false;
        }

        if (_shopNameHandles.TryGetValue(itemId, out var cached) && cached.Text == text)
        {
            name = (byte*)cached.Handle.AddrOfPinnedObject();
            return true;
        }
        // The shop renderer may retain a returned pointer for the rest of the current frame.
        // Keep replaced strings pinned until the controller has fully closed.
        if (cached.Handle.IsAllocated) _retiredShopNameHandles.Add(cached.Handle);
        var handle = GCHandle.Alloc(Encoding.UTF8.GetBytes(text + '\0'), GCHandleType.Pinned);
        _shopNameHandles[itemId] = (text, handle);
        name = (byte*)handle.AddrOfPinnedObject();
        return true;
    }

    private static bool TryGetConfidantShopCmmId(ushort itemId, out ushort cmmId)
    {
        var index = itemId - ConfidantShopMarkerFirst;
        if (index < ConfidantShopCmmIds.Length)
        {
            cmmId = ConfidantShopCmmIds[index];
            return true;
        }
        cmmId = 0;
        return false;
    }

    public void ClearConfidantShopNames()
    {
        foreach (var cached in _shopDescriptionHandles.Values)
            foreach (var handle in cached.Lines)
                if (handle.IsAllocated) handle.Free();
        _shopDescriptionHandles.Clear();
        foreach (var cached in _shopNameHandles.Values)
            if (cached.Handle.IsAllocated) cached.Handle.Free();
        _shopNameHandles.Clear();
        foreach (var handle in _retiredShopNameHandles)
            if (handle.IsAllocated) handle.Free();
        _retiredShopNameHandles.Clear();
    }

    public void Dispose()
    {
        SetShopDescriptionsEnabled(false);
        ClearRewardName();
        ClearConfidantShopNames();
    }
}
