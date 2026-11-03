using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

/// <summary>
/// Resolves P5R's canonical character records and active roster from validated Common
/// getter implementations. Pending damage runs after a native maximum-HP read, without
/// starting another flow interpreter during battle. Writes match native SET_HP's field.
/// </summary>
internal unsafe sealed class PartyHpManipulator : IDisposable
{
    private const string FlowFunctionGroupsPattern = "4C 8D 3D ?? ?? ?? ?? 8B F5";
    private const int GetHpFunctionId = 0x37;
    private const int GetMaxHpFunctionId = 0x38;
    private const int SetHpFunctionId = 0x39;
    private const int GetPartyFunctionId = 0x43;
    private const int CharacterRecordStride = 0x2A0;
    private const int HpOffset = 0x0C;

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
    private delegate int GetMaximumHp(IntPtr characterRecord);

    private readonly GetMaximumHp _getMaxHp;
    private readonly IHook<GetMaximumHp> _maximumHpHook;
    private readonly IntPtr _characterRecords;
    private readonly IntPtr _activeRoster;
    private readonly object _callLock = new();
    private readonly Log _log;

    public event Action? GameplayHpRead;

    public PartyHpManipulator(IReloadedHooks hooks, AddressScanner scanner, Log log)
    {
        _log = log;
        var groupsInstruction = scanner.FindUnique("FlowFunctionGroups.PartyHp", FlowFunctionGroupsPattern);
        var groups = (FlowFunctionGroup*)AddressScanner.ResolveRipRelative(groupsInstruction, 3, 7);
        var common = groups[0];
        if (common.Functions is null || common.NumFunctions <= GetPartyFunctionId)
            throw new InvalidOperationException("Common flow-function table is smaller than expected.");

        var getHp = Resolve(common, GetHpFunctionId, "GET_HP", 1);
        var getMaxHp = Resolve(common, GetMaxHpFunctionId, "GET_MAXHP", 1);
        var setHp = Resolve(common, SetHpFunctionId, "SET_HP", 2);
        var getParty = Resolve(common, GetPartyFunctionId, "GET_PARTY", 1);
        RequireBytes(getHp + 0x15, 0x48, 0x69, 0xC1, 0xA0, 0x02, 0x00, 0x00);
        RequireBytes(getHp + 0x1C, 0x48, 0x8D, 0x0D);
        RequireBytes(getMaxHp + 0x15, 0x48, 0x8D, 0x05);
        RequireBytes(getMaxHp + 0x1F, 0xE8);
        RequireBytes(setHp + 0x27, 0x48, 0x8D, 0x0D);
        RequireBytes(setHp + 0x3D, 0x44, 0x89, 0x4C, 0x0A, 0x0C);
        RequireBytes(getParty + 0x0E, 0x48, 0x8D, 0x05);
        _characterRecords = AddressScanner.ResolveRipRelative(getMaxHp + 0x15, 3, 7);
        var setterRecords = AddressScanner.ResolveRipRelative(setHp + 0x27, 3, 7);
        var getterHp = AddressScanner.ResolveRipRelative(getHp + 0x1C, 3, 7);
        if (setterRecords != _characterRecords || getterHp != _characterRecords + HpOffset)
            throw new InvalidOperationException("HP getter/setter character records do not match.");
        _activeRoster = AddressScanner.ResolveRipRelative(getParty + 0x0E, 3, 7);
        var helper = AddressScanner.ResolveRipRelative(getMaxHp + 0x1F, 1, 5);
        // Installed Steam build exposes this helper as an E9 thunk. Reject a different
        // entry layout before installing a hook instead of patching an unknown function.
        RequireBytes(helper, 0xE9);
        _maximumHpHook = hooks.CreateHook<GetMaximumHp>(GetMaximumHpImpl, helper);
        // Use the trampoline for our own reads: they must not recursively dispatch damage.
        _getMaxHp = _maximumHpHook.OriginalFunction;
        _maximumHpHook.Activate();
        log.Info("Resolved canonical HP records and active party roster through validated native getters/setter.");
    }

    private int GetMaximumHpImpl(IntPtr record)
    {
        var result = _getMaxHp(record);
        var offset = record.ToInt64() - _characterRecords.ToInt64();
        // Ignore enemy records, temporary copies, and navigator queries. Do not dispatch
        // until P5R itself has successfully read an actual combatant's canonical record.
        if (offset >= CharacterRecordStride && offset <= 10 * CharacterRecordStride &&
            offset % CharacterRecordStride == 0 && offset != 8 * CharacterRecordStride)
        {
            try { GameplayHpRead?.Invoke(); }
            catch (Exception ex) { _log.Error($"Native HP-read callback failed safely: {ex.Message}"); }
        }
        return result;
    }

    /// <returns>The number of living active members whose HP write was verified.</returns>
    public int ApplyMaxHpPercentageDamage(int percent)
    {
        lock (_callLock)
        {
            var members = new[]
            {
                1,
                (int)(ushort)Marshal.ReadInt16(_activeRoster + 2),
                (int)(ushort)Marshal.ReadInt16(_activeRoster + 4),
                (int)(ushort)Marshal.ReadInt16(_activeRoster + 6),
            };

            var writes = new List<(int Member, IntPtr Record, int Current, int Maximum, int Target)>();
            foreach (var member in members.Distinct())
            {
                if (member <= 0 || member > 10 || member == 8)
                    continue;

                var record = _characterRecords + member * CharacterRecordStride;
                var currentHp = Marshal.ReadInt32(record + HpOffset);
                if (currentHp <= 0)
                    continue;

                var maxHp = _getMaxHp(record);
                if (maxHp <= 0 || maxHp > 9999 || currentHp > 9999)
                    throw new InvalidOperationException($"Invalid HP record for member {member}: {currentHp}/{maxHp}.");
                var targetHp = DeathLinkDamageReceiver.TargetHp(currentHp, maxHp, percent);
                writes.Add((member, record, currentHp, maxHp, targetHp));
            }

            // Resolve and validate the complete batch before any write. A failed native
            // maximum-HP read can then be retried without damaging earlier members twice.
            var changed = 0;
            foreach (var write in writes)
            {
                Marshal.WriteInt32(write.Record + HpOffset, write.Target);
                var actualHp = Marshal.ReadInt32(write.Record + HpOffset);
                if (actualHp == write.Target)
                    changed++;
                _log.Info($"DeathLink HP member {write.Member}: {write.Current}/{write.Maximum} -> {actualHp}/{write.Maximum} (target {write.Target}).");
            }

            return changed;
        }
    }

    public void Dispose()
    {
        GameplayHpRead = null;
        _maximumHpHook.Disable();
    }

    private static void RequireBytes(IntPtr address, params byte[] expected)
    {
        for (var i = 0; i < expected.Length; i++)
            if (Marshal.ReadByte(address + i) != expected[i])
                throw new InvalidOperationException("Native HP accessor instruction layout is unsupported.");
    }

    private static IntPtr Resolve(FlowFunctionGroup group,
        int index, string expectedName, int expectedArguments)
    {
        var entry = group.Functions[index];
        var name = Marshal.PtrToStringAnsi(entry.Name);
        if (!string.Equals(name, expectedName, StringComparison.Ordinal) ||
            entry.NumArguments != expectedArguments)
            throw new InvalidOperationException(
                $"Common[0x{index:X}] identity mismatch (name '{name ?? "<null>"}', args {entry.NumArguments}).");
        return entry.Function;
    }
}
