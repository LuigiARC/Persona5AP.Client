using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X86;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

internal unsafe sealed class TboxFlagReader
{
    private const string GetTboxFlagPattern =
        "48 83 EC 28 E8 ?? ?? ?? ?? 48 85 C0 74 ?? 4C 8B 48 ?? 4D 85 C9 74 ?? 49 8B 91 ?? ?? ?? ??";

    private const string FlowCommandReferencePattern =
        "4C 8B 05 ?? ?? ?? ?? 41 8B 50 ?? 29 CA";

    [Function(CallingConventions.Fastcall)]
    private delegate long GetTboxFlagFlow();

    private readonly GetTboxFlagFlow _getTboxFlag;
    private readonly FlowCommandData** _flowCommandReference;
    private readonly object _callLock = new();

    public TboxFlagReader(IReloadedHooks hooks, AddressScanner scanner, Log log)
    {
        var getTboxAddress = scanner.FindUnique("FLD_GET_TBOX_FLAG", GetTboxFlagPattern);
        _getTboxFlag = hooks.CreateWrapper<GetTboxFlagFlow>(getTboxAddress, out _);

        var loadInstruction = scanner.FindUnique("FlowCommandData reference", FlowCommandReferencePattern);
        _flowCommandReference = (FlowCommandData**)AddressScanner.ResolveRipRelative(loadInstruction, 3, 7);
        log.Info("Resolved the flow-command pointer through RIP-relative addressing; no fixed data offset is used.");
    }

    public uint ReadCurrentFlag()
    {
        lock (_callLock)
        {
            var previous = *_flowCommandReference;
            var handle = GCHandle.Alloc(new FlowCommandData(), GCHandleType.Pinned);
            try
            {
                var temporary = (FlowCommandData*)handle.AddrOfPinnedObject();
                temporary->StackSize = 1;
                *_flowCommandReference = temporary;
                _getTboxFlag();
                return unchecked((uint)temporary->ReturnValue);
            }
            finally
            {
                *_flowCommandReference = previous;
                handle.Free();
            }
        }
    }
}
