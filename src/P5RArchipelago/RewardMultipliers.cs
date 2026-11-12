namespace P5RArchipelago;

/// <summary>Seed options, published together after successful slot validation.</summary>
internal sealed record RewardMultipliers(int Xp, int Money, int Items)
{
    public static readonly RewardMultipliers Vanilla = new(1, 1, 1);

    public static bool TryParse(IDictionary<string, object> slot, int protocol, out RewardMultipliers result)
    {
        result = Vanilla;
        bool Read(string key, out int value)
        {
            value = 1;
            if (!slot.TryGetValue(key, out var raw)) return protocol < 13;
            return int.TryParse(Convert.ToString(raw), out value) && value is >= 1 and <= 10;
        }
        if (!Read("xp_multiplier", out var xp) || !Read("money_multiplier", out var money) ||
            !Read("item_multiplier", out var items)) return false;
        result = new(xp, money, items);
        return true;
    }
}
