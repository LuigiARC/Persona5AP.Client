using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions.X64;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

/// <summary>Read-only field/control guard. Never changes what the F9 hotkey can open.</summary>
internal sealed class GameplayReadyProbe
{
    [Function(CallingConventions.Microsoft)]
    private delegate IntPtr GetField();
    internal const string CampMenuUpdatePattern =
        "48 83 EC 28 80 3D ?? ?? ?? ?? 00 0F 84 ?? ?? ?? ?? 80 3D ?? ?? ?? ?? 00 0F 85 ?? ?? ?? ?? 48 89 5C 24 30 48 8D 0D ?? ?? ?? ?? 48 89 6C 24 38 48 89 74 24 40 48 89 7C 24 48 33 FF 4C 89 74 24 20";
    private readonly IntPtr _sequenceRoot;
    private readonly IntPtr _campMenuActive;
    private readonly GetField _getField;
    private string _diagnostic = "not sampled (an earlier delivery guard may be blocking)";
    private long _nextDiagnostic;
    public string Diagnostic => Volatile.Read(ref _diagnostic);

    public GameplayReadyProbe(IReloadedHooks hooks, AddressScanner scanner)
    {
        var root = scanner.FindUnique("SequenceRoot.AutomaticRewards",
            "48 8B 05 ?? ?? ?? ?? 48 8B 48 48 0F B6 01 24 01 C3");
        _sequenceRoot = AddressScanner.ResolveRipRelative(root, 3, 7);
        // CAMP_MAIN sets this byte before creating its task and clears it on
        // destruction, covering opening and closing as well as visible submenus.
        var campUpdate = scanner.FindUnique("CampMenuUpdate.AutomaticRewards", CampMenuUpdatePattern);
        _campMenuActive = AddressScanner.ResolveRipRelative(campUpdate + 4, 2, 7);
        var padLock = scanner.FindUnique("FieldPadLock.AutomaticRewards",
            "48 83 EC 28 E8 ?? ?? ?? ?? 48 85 C0 0F 84 BA 00 00 00 48 89 5C 24 30 48 8B 58 48 48 85 DB");
        _getField = hooks.CreateWrapper<GetField>(AddressScanner.ResolveRipRelative(padLock + 4, 1, 5), out _);
    }

    public bool IsReady()
    {
        var sequence = Marshal.ReadIntPtr(_sequenceRoot);
        var info = sequence == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(sequence + 0x48);
        var flags = info == IntPtr.Zero ? (byte)0 : Marshal.ReadByte(info);
        // Sequence bit0 is not a field-presence test: it is zero during verified
        // free movement in Mementos. Retain it for diagnostics only.
        var owner = _getField();
        var field = owner == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(owner + 0x48);
        var locks = field == IntPtr.Zero ? 0L : Marshal.ReadInt64(field + 0x1E0);
        // Native FLD_SMP_EVENT_BEGIN sets bit0 at +16ED0; END clears it.
        var eventFlags = field == IntPtr.Zero ? (ushort)0 : unchecked((ushort)Marshal.ReadInt16(field + 0x16ED0));
        var campMenuActive = Marshal.ReadByte(_campMenuActive) != 0;
        var ready = FieldRewardSafety.IsReady(field != IntPtr.Zero, locks, eventFlags, campMenuActive);
        var now = Environment.TickCount64;
        if (now >= _nextDiagnostic)
        {
            _nextDiagnostic = now + 500;
            Volatile.Write(ref _diagnostic,
                $"sampleTick={now}, ready={ready}, sequence=0x{sequence:X}, info=0x{info:X}, " +
                $"sequenceFlags=0x{flags:X2} (diagnostic only), fieldPresent={field != IntPtr.Zero}, " +
                $"owner=0x{owner:X}, field=0x{field:X}, locks=0x{locks:X16}, " +
                $"padLocked={(locks & 0x400000000L) != 0}, movementLocked={(locks & 0x800000000L) != 0}, " +
                $"eventFlags=0x{eventFlags:X4}, fieldEventActive={(eventFlags & 1) != 0}, campMenuActive={campMenuActive}");
        }
        return ready;
    }
}
