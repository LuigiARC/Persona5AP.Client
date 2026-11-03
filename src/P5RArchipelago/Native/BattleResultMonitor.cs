using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X86;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

/// <summary>Observes P5R's final battle-result byte. Native result 2 is the party-defeat
/// route (FLD_GET_BATTLE_RESULT exposes it to flow scripts as result 1).</summary>
internal sealed unsafe class BattleResultMonitor
{
    private const string EndBattlePattern = "40 53 55 41 57 48 81 EC 80 00 00 00";

    [Function(CallingConventions.Fastcall)]
    private delegate ulong EndBattle(byte* battleResult);

    private readonly IHook<EndBattle> _endBattleHook;
    private readonly Log _log;

    public event Action? PartyWiped;

    public BattleResultMonitor(IReloadedHooks hooks, AddressScanner scanner, Log log)
    {
        _log = log;
        var address = scanner.FindUnique("EndBattle", EndBattlePattern);
        _endBattleHook = hooks.CreateHook<EndBattle>(EndBattleImpl, address).Activate();
    }

    private ulong EndBattleImpl(byte* battleResult)
    {
        var result = battleResult is null ? byte.MaxValue : *battleResult;
        var originalResult = _endBattleHook.OriginalFunction(battleResult);
        _log.Info($"Battle ended with native result {result}.");

        if (result == 2)
        {
            try
            {
                PartyWiped?.Invoke();
            }
            catch (Exception ex)
            {
                // DeathLink must never interfere with P5R's own battle teardown.
                _log.Error($"Party-wipe DeathLink handler failed safely: {ex.Message}");
            }
        }

        return originalResult;
    }
}
