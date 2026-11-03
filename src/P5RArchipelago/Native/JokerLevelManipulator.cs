using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions.X64;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

/// <summary>Joker's battle growth commit, invoked only by the hub's game-thread callback.
/// Zeroed result data supplies EXP only: no money, social stats, Persona EXP, or UI.</summary>
internal unsafe sealed class JokerLevelManipulator
{
    internal const string GrowthPattern = "48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 57 41 56 41 57 48 83 EC 20 8B 1D ?? ?? ?? ?? 48 8B F2 03 59 04";
    [StructLayout(LayoutKind.Sequential)]
    private struct Group { public Entry* Functions; public long Count; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Entry { public IntPtr Function; public long Arguments; public IntPtr Name; }
    [Function(CallingConventions.Microsoft)]
    private delegate void CommitGrowth(IntPtr experienceResult, IntPtr rewardsResult);
    private readonly CommitGrowth _commit;
    private readonly IntPtr _level;
    private readonly IntPtr _experience;
    private readonly IntPtr _curve;

    public JokerLevelManipulator(IReloadedHooks hooks, AddressScanner scanner)
    {
        var anchor = scanner.FindUnique("FlowFunctionGroups.JokerLevels", "4C 8D 3D ?? ?? ?? ?? 8B F5");
        var common = ((Group*)AddressScanner.ResolveRipRelative(anchor, 3, 7))[0];
        if (common.Functions is null || common.Count <= 0x17A)
            throw new InvalidOperationException("Common level function table is unsupported.");
        var getter = Resolve(common, 0x17A, "GET_PLAYER_LV", 2);
        var wanted = Resolve(common, 0x12F, "GET_HERO_WANTED_EXP", 1);
        Require(getter + 0x20, 0x0F, 0xB7, 0x05);
        Require(wanted + 0x1A, 0x4C, 0x8D, 0x05);
        _level = AddressScanner.ResolveRipRelative(getter + 0x20, 3, 7);
        _curve = AddressScanner.ResolveRipRelative(wanted + 0x1A, 3, 7);
        var growth = scanner.FindUnique("JokerGrowthCommit", GrowthPattern);
        Require(growth + 0x24, 0x48, 0x8D, 0x15);
        Require(growth + 0x7F, 0x66, 0x89, 0x2D);
        _experience = AddressScanner.ResolveRipRelative(growth + 0x18, 2, 6);
        if (_experience != _level + 4 ||
            AddressScanner.ResolveRipRelative(growth + 0x24, 3, 7) != _curve ||
            AddressScanner.ResolveRipRelative(growth + 0x7F, 3, 7) != _level)
            throw new InvalidOperationException("Joker growth/getter record identity mismatch.");
        _commit = hooks.CreateWrapper<CommitGrowth>(growth, out _);
    }

    public int ApplyTarget(int target)
    {
        var before = (int)(ushort)Marshal.ReadInt16(_level);
        if (before is < 1 or > 99 || target < before || target > 99)
            throw new InvalidOperationException($"Invalid Joker level target {before} -> {target}.");
        if (target == before) return before;
        var threshold = Marshal.ReadInt32(_curve + (target - 1) * 4);
        var nextThreshold = target == 99 ? int.MaxValue : Marshal.ReadInt32(_curve + target * 4);
        var experience = Marshal.ReadInt32(_experience);
        if (threshold < 0 || nextThreshold <= threshold || experience < 0 || experience >= nextThreshold)
            throw new InvalidOperationException("Joker EXP/level curve is inconsistent; reward remains pending.");
        // The real growth commit reads EXP at +4 and reward fields through +0x4C.
        byte* result = stackalloc byte[0x80];
        new Span<byte>(result, 0x80).Clear();
        *(int*)(result + 4) = Math.Max(0, threshold - experience);
        _commit((IntPtr)result, (IntPtr)result);
        var actual = (int)(ushort)Marshal.ReadInt16(_level);
        if (actual != target)
            throw new InvalidOperationException($"Joker growth read-back Lv {actual}, expected {target}; reward remains pending.");
        return actual;
    }

    private static IntPtr Resolve(Group group, int index, string name, int arguments)
    {
        var entry = group.Functions[index];
        if (entry.Arguments != arguments || Marshal.PtrToStringAnsi(entry.Name) != name)
            throw new InvalidOperationException($"Level function identity mismatch: {name}.");
        return entry.Function;
    }
    private static void Require(IntPtr address, params byte[] expected)
    {
        for (var i = 0; i < expected.Length; i++)
            if (Marshal.ReadByte(address + i) != expected[i])
                throw new InvalidOperationException("Unsupported Joker growth instruction layout.");
    }
}
