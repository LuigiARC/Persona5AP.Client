using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

/// <summary>Keeps an automatic reward job alive while vanilla's resumable companion
/// level-up command waits for its animation or the player's skill choices.</summary>
internal sealed class PartyLevelUpMonitor : IDisposable
{
    internal const string StartLevelUpPattern =
        "48 89 5C 24 10 48 89 6C 24 18 57 48 81 EC F0 00 00 00 48 8B 05 ?? ?? ?? ?? 48 85 C0 74 50 83 B8 24 02 00 00 00";

    [Function(CallingConventions.Microsoft)]
    private delegate nuint StartLevelUp();

    private readonly IHook<StartLevelUp> _hook;
    private readonly Action _keepAlive;
    private readonly Log _log;

    public PartyLevelUpMonitor(IReloadedHooks hooks, AddressScanner scanner, Action keepAlive, Log log)
    {
        _keepAlive = keepAlive;
        _log = log;
        _hook = hooks.CreateHook<StartLevelUp>(StartLevelUpImpl,
            scanner.FindUnique("START_PC_LEVEL_UP", StartLevelUpPattern)).Activate();
    }

    private nuint StartLevelUpImpl()
    {
        var result = _hook.OriginalFunction();
        // Zero suspends the flow command; vanilla resumes it until its UI closes.
        if (result == 0)
        {
            try { _keepAlive(); }
            catch (Exception ex) { _log.Error($"Party level-up observer failed safely: {ex.Message}"); }
        }
        return result;
    }

    public void Dispose() => _hook.Disable();
}
