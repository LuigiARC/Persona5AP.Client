using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

/// <summary>Scales the shared battle/instakill result before vanilla calculates growth,
/// applies rewards and builds its UI. AP grants do not enter this function.</summary>
internal sealed unsafe class RewardMultiplierHook : IDisposable
{
    internal const string PrepareRewardsPattern =
        "40 55 41 56 41 57 48 83 EC 40 48 89 5C 24 60 4C 8B F1 48 89 74 24 68 48 8B F2 BA 50 0D 00 00";

    [Function(CallingConventions.Microsoft)]
    private delegate void PrepareRewards(nint growth, byte* rewards);

    private readonly IHook<PrepareRewards> _hook;
    private readonly Func<RewardMultipliers> _settings;
    private readonly Log _log;

    public RewardMultiplierHook(IReloadedHooks hooks, AddressScanner scanner,
        Func<RewardMultipliers> settings, Log log)
    {
        _settings = settings;
        _log = log;
        _hook = hooks.CreateHook<PrepareRewards>(PrepareRewardsImpl,
            scanner.FindUnique("PrepareBattleAndInstakillRewards", PrepareRewardsPattern));
        _hook.Activate();
    }

    private void PrepareRewardsImpl(nint growth, byte* rewards)
    {
        try
        {
            var settings = _settings();
            if (rewards != null) ScaleRewards(rewards, settings);
        }
        catch (Exception ex) { _log.Error($"Reward multipliers unavailable for this battle: {ex.Message}"); }
        _hook.OriginalFunction(growth, rewards);
    }

    internal static void ScaleRewards(byte* rewards, RewardMultipliers settings)
    {
        // Common 0x58-byte result: flags +0, EXP +4, money +8. Vanilla owns
        // bonus EXP/money +0xC/+0x10, levels, Persona acquisition and other rewards.
        // +0x38 is the separate EXP award for an acquired matching stock Persona.
        // Bound boosted totals before native integer arithmetic; 1x is byte-exact.
        if (settings.Xp != 1)
        {
            *(int*)(rewards + 4) = ScalePositive(*(int*)(rewards + 4), settings.Xp, 9_999_999);
            *(int*)(rewards + 0x38) = ScalePositive(*(int*)(rewards + 0x38), settings.Xp, 9_999_999);
        }
        if (settings.Money != 1)
            *(int*)(rewards + 8) = ScalePositive(*(int*)(rewards + 8), settings.Money, 9_999_999);
        if (settings.Items == 1) return;
        // Four item IDs followed by four u16 quantities. Native application clamps
        // inventory to 99. Preserve empty entries and never create new drop rolls.
        for (var i = 0; i < 4; i++)
        {
            var id = *(ushort*)(rewards + 0x3E + i * 2);
            var quantity = (ushort*)(rewards + 0x46 + i * 2);
            if (id != 0 && *quantity != 0)
                *quantity = (ushort)Math.Min((long)*quantity * settings.Items, short.MaxValue);
        }
    }

    private static int ScalePositive(int value, int multiplier, int maximum) =>
        value <= 0 ? value : (int)Math.Min((long)value * multiplier, maximum);

    public void Dispose() => _hook.Disable();
}
