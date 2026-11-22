using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Enums;
using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions.X64;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

internal sealed class BattleResultMonitor : IDisposable
{
    internal const string DefeatPhasePattern =
        "48 8B C4 48 89 58 20 48 89 50 10 48 89 48 08 55 56 57 41 54 41 55 41 56 41 57 48 8D A8 58 FB FF FF 48 81 EC 70 05 00 00 48 8B F2";

    [Function(CallingConventions.Microsoft)]
    private delegate void DefeatPhase(IntPtr phase, IntPtr battle);

    private readonly IAsmHook _defeatPhaseHook;
    private readonly IReverseWrapper<DefeatPhase> _observer;
    private readonly Func<bool> _scriptedLoss;
    private readonly Log _log;

    public event Action? PartyWiped;

    public BattleResultMonitor(IReloadedHooks hooks, AddressScanner scanner, Log log,
        Func<bool> scriptedLoss)
    {
        _log = log;
        _scriptedLoss = scriptedLoss;
        var address = scanner.FindUnique("BattleDefeatPhase", DefeatPhasePattern);
        _observer = hooks.CreateReverseWrapper<DefeatPhase>(DefeatPhaseImpl);
        _defeatPhaseHook = hooks.CreateAsmHook(new[]
        {
            "use64", "push rcx", "push rdx", "sub rsp, 40",
            $"mov rax, 0x{_observer.WrapperPointer:X}", "call rax",
            "add rsp, 40", "pop rdx", "pop rcx"
        }, address.ToInt64(), AsmHookBehaviour.ExecuteFirst).Activate();
    }

    private void DefeatPhaseImpl(IntPtr phase, IntPtr battle)
    {
        try
        {
            //flag 0x3000011C for party annihilation
            if (battle != IntPtr.Zero &&
                (Marshal.ReadInt32(battle, 0x268) & 0x01000000) == 0 && !_scriptedLoss())
            {
                _log.Info("Battle defeat reporting DeathLink.");
                PartyWiped?.Invoke();
            }
            else
            {
                _log.Info("Battle defeat but skipped scripted loss.");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"DeathLink handler failed: {ex.Message}");
        }
    }

    public void Dispose() => _defeatPhaseHook.Disable();
}
