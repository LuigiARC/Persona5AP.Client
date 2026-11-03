using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

/// <summary>
/// Palace boss endings return through Common[0x19] CALL_CALENDAR instead of the ordinary
/// dungeon-exit scripts. Suppress that calendar jump only for a Palace session explicitly
/// started by the AP hub and only after that same Palace's clear flag has been set, then enqueue
/// the hub BF's cleanup-and-return procedure. Every unrelated calendar call remains vanilla.
/// </summary>
internal unsafe sealed class PalaceCalendarReturnMonitor
{
    private const string FlowFunctionGroupsPattern = "4C 8D 3D ?? ?? ?? ?? 8B F5";
    private const int CallCalendarFunctionId = 0x19;
    public const uint ReturnToHubProcedureIndex = 26;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlowFunctionGroup
    {
        public FlowFunctionInfo* Functions;
        public long NumFunctions;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlowFunctionInfo
    {
        public IntPtr Function;
        public long NumArguments;
        public IntPtr Name;
    }

    [Function(CallingConventions.Microsoft)]
    private delegate bool FlowFunction(nuint ignored);

    private readonly IHook<FlowFunction> _hook;
    private readonly Func<PalaceLocation?> _activePalace;
    private readonly Func<PalaceLocation, bool> _isClear;
    private readonly Action<PalaceLocation> _reportClear;
    private readonly Action _clearSession;
    private readonly HubFlowRunner _hubFlow;
    private readonly Log _log;

    public PalaceCalendarReturnMonitor(IReloadedHooks hooks, AddressScanner scanner,
        Func<PalaceLocation?> activePalace, Func<PalaceLocation, bool> isClear,
        Action<PalaceLocation> reportClear, Action clearSession, HubFlowRunner hubFlow, Log log)
    {
        _activePalace = activePalace;
        _isClear = isClear;
        _reportClear = reportClear;
        _clearSession = clearSession;
        _hubFlow = hubFlow;
        _log = log;

        var groupsInstruction = scanner.FindUnique("FlowFunctionGroups", FlowFunctionGroupsPattern);
        var groupsAddress = AddressScanner.ResolveRipRelative(groupsInstruction, 3, 7);
        var groups = (FlowFunctionGroup*)groupsAddress;
        var common = groups[0];
        if (common.Functions is null || common.NumFunctions <= CallCalendarFunctionId)
            throw new InvalidOperationException("Common flow-function table is smaller than expected.");

        var entry = common.Functions[CallCalendarFunctionId];
        var name = Marshal.PtrToStringAnsi(entry.Name);
        if (!string.Equals(name, "CALL_CALENDAR", StringComparison.Ordinal) || entry.NumArguments != 0)
            throw new InvalidOperationException(
                $"Common[0x19] identity mismatch (name '{name ?? "<null>"}', args {entry.NumArguments}).");

        _hook = hooks.CreateHook<FlowFunction>(CallCalendarImpl, entry.Function).Activate();
        _log.Info("Resolved CALL_CALENDAR through validated Common flow table entry 0x19.");
    }

    private bool CallCalendarImpl(nuint ignored)
    {
        var palace = _activePalace();
        if (palace is null || !_isClear(palace))
            return _hook.OriginalFunction(ignored);

        // Reporting is deliberately isolated from the field redirect. A transient AP/state
        // failure must never strand the player on the vanilla calendar timeline.
        try
        {
            // Some boss scripts set the clear bit through a path which does not execute the
            // hooked BIT_ON flow command (Kamoshida is the confirmed case). Reaching this
            // Palace-scoped post-boss CALL_CALENDAR with the matching clear bit is equally
            // definitive, so report the AP location here as an idempotent fallback.
            _reportClear(palace);
        }
        catch (Exception ex)
        {
            _log.Error($"Could not report '{palace.Name}' Palace Clear from its post-boss return: {ex.Message}");
        }

        try
        {
            // Clear durable context before enqueueing. If the field transition itself fails,
            // allowing a later unrelated CALL_CALENDAR to be swallowed would be more harmful.
            _clearSession();
            _hubFlow.Run(ReturnToHubProcedureIndex);
            _log.Info($"Redirected '{palace.Name}' post-boss CALL_CALENDAR to the Mementos hub.");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"Could not redirect '{palace.Name}' post-boss return; falling back to the calendar: {ex.Message}");
            return _hook.OriginalFunction(ignored);
        }
    }
}
