using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions.X86;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

/// <summary>
/// Owns the pinned locally reconstructed hub BF and P5R's flow-procedure enqueueing entry point. Both the
/// keyboard shortcut and the post-boss calendar interception use this one runner, ensuring the
/// BF remains pinned for the full lifetime of either caller.
/// </summary>
internal sealed class HubFlowRunner : IDisposable
{
    private const string RunFlowProcedurePattern =
        "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 48 89 7C 24 ?? 41 56 48 83 EC 60 41 89 CE";

    [Function(CallingConventions.Fastcall)]
    private delegate IntPtr RunFlowProcedure(int mode, IntPtr file, uint fileSize, uint procedureIndex);

    private readonly RunFlowProcedure _runFlowProcedure;
    private readonly GCHandle _hubBfHandle;
    private readonly uint _hubBfLength;

    public HubFlowRunner(IReloadedHooks hooks, AddressScanner scanner, string path)
    {
        var runAddress = scanner.FindUnique("RunFlowProcedure", RunFlowProcedurePattern);
        _runFlowProcedure = hooks.CreateWrapper<RunFlowProcedure>(runAddress, out _);

        if (!File.Exists(path))
            throw new InvalidOperationException("Persona 5 AP's automatically prepared hub script is missing from its local cache.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0)
            throw new InvalidOperationException("Locally prepared Persona 5 AP hub BF is empty.");
        _hubBfHandle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        _hubBfLength = checked((uint)bytes.Length);
    }

    public void Run(uint procedureIndex) =>
        _runFlowProcedure(8, _hubBfHandle.AddrOfPinnedObject(), _hubBfLength, procedureIndex);

    public void Dispose()
    {
        if (_hubBfHandle.IsAllocated) _hubBfHandle.Free();
    }
}
