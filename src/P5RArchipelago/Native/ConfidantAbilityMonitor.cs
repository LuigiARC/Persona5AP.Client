using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X86;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

internal sealed class ConfidantAbilityMonitor
{
    private const string CheckEnabledPattern = "40 53 55 56 41 54 41 56 48 83 EC 20";
    private const string SetLevelPattern = "66 85 C9 0F 84 ?? ?? ?? ?? 57";
    private const string OpenPattern =
        "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 48 8B 35 ?? ?? ?? ?? 33 FF 0F B7 D9";

    [Function(CallingConventions.Fastcall)]
    private delegate long CheckEnabled(uint featureId);

    [Function(CallingConventions.Fastcall)]
    private delegate IntPtr SetLevel(ushort cmmId, short rank);

    [Function(CallingConventions.Fastcall)]
    private delegate void Open(ushort cmmId);

    private readonly IHook<CheckEnabled> _checkHook;
    private readonly IHook<SetLevel> _setLevelHook;
    private readonly IHook<Open> _openHook;
    private readonly ConfidantAbilityCatalog _catalog;
    private readonly Func<uint, long?> _syntheticOverride;

    public event Action<ConfidantAbilityLocation>? AbilityAcquired;

    /// <summary>Fires with the affected CMM.BIN id on every CMM_SET_LV call, hub-triggered or
    /// real -- used to pace the hub's "Spend time with a Confidant" menu, per Confidant, against
    /// AP check progress (see ArchipelagoMod).</summary>
    public event Action<ushort>? RankSet;

    /// <summary>
    /// Hooks the real CMM_CHECK_ENABLED so mod-reserved synthetic feature IDs (see
    /// <see cref="PalaceLocation.AccessSyntheticFeatureId"/>) can answer from AP-received-item
    /// state instead of the game's own confidant data, while every real feature ID still passes
    /// straight through to the original function unmodified. This replaces an earlier attempt to
    /// bridge the same gating through BIT_CHK, which was abandoned: flow-script's own BIT_CHK
    /// opcode never calls the hooked native BIT_CHK helper at all. Flow-script's
    /// CMM_CHECK_ENABLE_FUNC builtin, by contrast, does call through to this same hooked
    /// CMM_CHECK_ENABLED address (confirmed in-game), so Palace access gating is driven from the
    /// hub's flow script that way -- see FSCR8000_002_000.flow's palace_menu().
    /// </summary>
    public ConfidantAbilityMonitor(IReloadedHooks hooks, AddressScanner scanner,
        ConfidantAbilityCatalog catalog, Func<uint, long?> syntheticOverride)
    {
        _catalog = catalog;
        _syntheticOverride = syntheticOverride;
        _checkHook = hooks.CreateHook<CheckEnabled>(CheckEnabledImpl,
            scanner.FindUnique("CMM_CHECK_ENABLED", CheckEnabledPattern)).Activate();
        _setLevelHook = hooks.CreateHook<SetLevel>(SetLevelImpl,
            scanner.FindUnique("CMM_SET_LV", SetLevelPattern)).Activate();
        _openHook = hooks.CreateHook<Open>(OpenImpl,
            scanner.FindUnique("CMM_OPEN", OpenPattern)).Activate();
    }

    private long CheckEnabledImpl(uint featureId)
    {
        var overridden = _syntheticOverride(featureId);
        if (overridden.HasValue)
            return overridden.Value;

        // This function is a read-only predicate used constantly by menus, field logic, and
        // save loading. A true result means an ability already exists, not that it was just
        // acquired. Reporting here caused Save 13's story-granted Fool/Magician/Moon abilities
        // to be sent merely because P5R queried them. Actual acquisitions are emitted only by
        // the CMM_OPEN/CMM_SET_LV mutation hooks below.
        return _checkHook.OriginalFunction(featureId);
    }

    private IntPtr SetLevelImpl(ushort cmmId, short rank)
    {
        var result = _setLevelHook.OriginalFunction(cmmId, rank);
        RankSet?.Invoke(cmmId);
        ReportRank(cmmId, rank);
        return result;
    }

    private void OpenImpl(ushort cmmId)
    {
        _openHook.OriginalFunction(cmmId);
        RankSet?.Invoke(cmmId);
        ReportRank(cmmId, 1);
    }

    public void AdvanceToRank(ushort cmmId, short currentRank, short targetRank)
    {
        if (targetRank <= currentRank) return;

        if (currentRank <= 0)
        {
            OpenImpl(cmmId);
            if (targetRank > 1)
                SetLevelImpl(cmmId, targetRank);
        }
        else
        {
            SetLevelImpl(cmmId, targetRank);
        }
    }

    private void ReportRank(ushort cmmId, short rank)
    {
        foreach (var location in _catalog.AtRank(cmmId, rank))
            AbilityAcquired?.Invoke(location);
    }

    public IEnumerable<ConfidantAbilityLocation> ReadCurrentlyEnabled()
    {
        foreach (var location in _catalog.Locations)
            if (_checkHook.OriginalFunction(location.FeatureId) != 0)
                yield return location;
    }
}
