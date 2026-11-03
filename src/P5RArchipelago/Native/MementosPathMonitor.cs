using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

/// <summary>Caches the real Mementos Path id returned by Field FLD_GET_DNG_NO.</summary>
internal unsafe sealed class MementosPathMonitor
{
    private const string FlowFunctionGroupsPattern = "4C 8D 3D ?? ?? ?? ?? 8B F5";
    private const string GetFlowIntArgumentPattern = "4C 8B 05 ?? ?? ?? ?? 41 8B 50 ?? 29 CA";
    // Full opcode0x102A belongs to Field; its index inside that group is0x2A.
    private const int GetDungeonNumberFunctionId = 0x2A;

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
    private delegate IntPtr GetField();

    private readonly IHook<FlowFunction> _hook;
    private readonly FlowCommandData** _flowCommandReference;
    private int _currentPath;
    private readonly GetField _getField;
    public int CachedPath => Volatile.Read(ref _currentPath);

    public int CurrentPath {
        get {
            // Same field owner and ushort read as FLD_GET_DNG_NO, without changing
            // interpreter state or waiting for a vanilla script to call that opcode.
            var owner = _getField();
            var field = owner == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(owner + 0x48);
            if (field == IntPtr.Zero) { Volatile.Write(ref _currentPath, 0); return 0; }
            var path = (ushort)Marshal.ReadInt16(field + 0x1FE);
            var current = path is >= 1 and <= 9 ? path : 0;
            Volatile.Write(ref _currentPath, current);
            return current;
        }
    }

    /// <summary>
    /// Records a Path selected by the AP checkpoint menu before CALL_AT_DUNGEON changes
    /// fields. The vanilla FLD_GET_DNG_NO hook remains the authority during ordinary
    /// Mementos traversal, but a direct checkpoint load can expose a chest before any
    /// field script asks for the dungeon number.
    /// </summary>
    public void SetCurrentPath(int path)
    {
        if (path is < 0 or > 9)
            return;
        Volatile.Write(ref _currentPath, path);
    }

    public MementosPathMonitor(IReloadedHooks hooks, AddressScanner scanner, Log log)
    {
        var groupsInstruction = scanner.FindUnique("FlowFunctionGroups", FlowFunctionGroupsPattern);
        var groups = (FlowFunctionGroup*)AddressScanner.ResolveRipRelative(groupsInstruction, 3, 7);
        var field = groups[1];
        if (field.Functions is null || field.NumFunctions <= GetDungeonNumberFunctionId)
            throw new InvalidOperationException("Field flow-function table is smaller than expected.");

        var entry = field.Functions[GetDungeonNumberFunctionId];
        var name = Marshal.PtrToStringAnsi(entry.Name);
        if (!string.Equals(name, "FLD_GET_DNG_NO", StringComparison.Ordinal) || entry.NumArguments != 0)
            throw new InvalidOperationException(
                $"Field[0x102A] identity mismatch (name '{name ?? "<null>"}', args {entry.NumArguments}).");

        byte[] read = [0x0F, 0xB7, 0x81, 0xFE, 0x01, 0, 0];
        if (Marshal.ReadByte(entry.Function + 4) != 0xE8 ||
            read.Where((value, index) => Marshal.ReadByte(entry.Function + 0x37 + index) != value).Any())
            throw new InvalidOperationException("Unsupported live Mementos path getter layout.");
        var getter = entry.Function + 9 + Marshal.ReadInt32(entry.Function + 5);
        _getField = hooks.CreateWrapper<GetField>(getter, out _);

        var flowInstruction = scanner.FindUnique("GetFlowscriptInt4Arg", GetFlowIntArgumentPattern);
        _flowCommandReference = (FlowCommandData**)AddressScanner.ResolveRipRelative(flowInstruction, 3, 7);
        _hook = hooks.CreateHook<FlowFunction>(GetDungeonNumberImpl, entry.Function).Activate();
        log.Info("Resolved FLD_GET_DNG_NO through validated Field flow table entry 0x102A.");
    }

    private bool GetDungeonNumberImpl(nuint ignored)
    {
        var result = _hook.OriginalFunction(ignored);
        if (result && *_flowCommandReference is not null)
        {
            var path = checked((int)(*_flowCommandReference)->ReturnValue);
            if (path is >= 1 and <= 9)
                Volatile.Write(ref _currentPath, path);
        }
        return result;
    }
}
