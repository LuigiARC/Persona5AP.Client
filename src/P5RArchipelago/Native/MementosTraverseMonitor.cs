using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

/// <summary>
/// Virtualizes Field 0x250 (FLD_CHECK_DNG_TRAVERSE) for Mementos Paths unlocked by
/// Progressive Mementos Access. The original still runs first; only a false result for
/// target Paths 2..9 can be promoted to true. No campaign or Palace-clear flag is written.
/// </summary>
internal unsafe sealed class MementosTraverseMonitor
{
    private const string FlowFunctionGroupsPattern = "4C 8D 3D ?? ?? ?? ?? 8B F5";
    private const string GetFlowIntArgumentPattern = "4C 8B 05 ?? ?? ?? ?? 41 8B 50 ?? 29 CA";
    private const int CheckDungeonTraverseFunctionId = 0x250;

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

    [Function(CallingConventions.Microsoft)]
    private delegate int GetFlowIntArgument(byte argumentIndex);

    private readonly IHook<FlowFunction> _hook;
    private readonly GetFlowIntArgument _getArgument;
    private readonly FlowCommandData** _flowCommandReference;
    private readonly Func<int> _accessCount;
    private readonly Log _log;

    public MementosTraverseMonitor(IReloadedHooks hooks, AddressScanner scanner,
        Func<int> accessCount, Log log)
    {
        _accessCount = accessCount;
        _log = log;

        var groupsInstruction = scanner.FindUnique("FlowFunctionGroups", FlowFunctionGroupsPattern);
        var groupsAddress = AddressScanner.ResolveRipRelative(groupsInstruction, 3, 7);
        var groups = (FlowFunctionGroup*)groupsAddress;
        var field = groups[1];
        if (field.Functions is null || field.NumFunctions <= CheckDungeonTraverseFunctionId)
            throw new InvalidOperationException("Field flow-function table is smaller than expected.");

        var entry = field.Functions[CheckDungeonTraverseFunctionId];
        var name = Marshal.PtrToStringAnsi(entry.Name);
        if (!string.Equals(name, "FLD_CHECK_DNG_TRAVERSE", StringComparison.Ordinal) || entry.NumArguments != 2)
            throw new InvalidOperationException(
                $"Field[0x250] identity mismatch (name '{name ?? "<null>"}', args {entry.NumArguments}).");

        var getArgumentInstruction = scanner.FindUnique("GetFlowscriptInt4Arg", GetFlowIntArgumentPattern);
        _getArgument = hooks.CreateWrapper<GetFlowIntArgument>(getArgumentInstruction, out _);
        _flowCommandReference = (FlowCommandData**)AddressScanner.ResolveRipRelative(
            getArgumentInstruction, 3, 7);
        _hook = hooks.CreateHook<FlowFunction>(CheckTraverseImpl, entry.Function).Activate();
        _log.Info("Resolved FLD_CHECK_DNG_TRAVERSE through validated Field flow table entry 0x250.");
    }

    private bool CheckTraverseImpl(nuint ignored)
    {
        int targetPath;
        try
        {
            targetPath = _getArgument(0);
        }
        catch (Exception ex)
        {
            _log.Error($"Could not inspect FLD_CHECK_DNG_TRAVERSE arguments: {ex.Message}");
            return _hook.OriginalFunction(ignored);
        }

        var succeeded = _hook.OriginalFunction(ignored);
        if (!succeeded || targetPath is < 2 or > 9 || *_flowCommandReference is null)
            return succeeded;

        var requiredTier = targetPath - 1;
        var have = _accessCount();
        if (have >= requiredTier && (*_flowCommandReference)->ReturnValue == 0)
        {
            (*_flowCommandReference)->ReturnValue = 1;
            _log.Info($"Mementos traverse virtualized: Path {targetPath}, tier {requiredTier}, have {have}.");
        }

        return succeeded;
    }
}
