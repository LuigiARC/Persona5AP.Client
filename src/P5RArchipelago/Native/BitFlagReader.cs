using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X86;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

internal sealed class BitFlagReader
{
    private const string BitCheckPattern =
        "4C 8D 05 ?? ?? ?? ?? 33 C0 49 8B D0 0F 1F 40 00 39 0A 74 ?? FF C0 48 83 C2 08 83 F8 10 72 ?? 8B D1";

    [Function(CallingConventions.Fastcall)]
    private delegate byte BitCheck(uint bitIndex);

    private readonly BitCheck _check;

    // NOTE: this is a plain wrapper (not a hook), used only for reconciliation reads of real
    // game flags from C#. It was briefly converted into a hook with a synthetic-flag override
    // for AP item-gating, but that was abandoned: flow-script's own BIT_CHK() opcode does not
    // call through this same native address at all (confirmed empirically -- thousands of real
    // BIT_CHK calls were observed via diagnostic logging, never once with a mod-reserved value),
    // so a hook here can never intercept anything flow-script itself checks. It also hurt
    // framerate noticeably once hooked, since this address is called extremely often. See
    // PHASE_STATUS.md for the CMM_CHECK_ENABLE_FUNC-based replacement.
    public BitFlagReader(IReloadedHooks hooks, AddressScanner scanner)
    {
        var address = scanner.FindUnique("BIT_CHK", BitCheckPattern);
        _check = hooks.CreateWrapper<BitCheck>(address, out _);
    }

    public bool IsSet(uint flag) => _check(flag) != 0;
}
