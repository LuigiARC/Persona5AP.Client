namespace P5RArchipelago;

/// <summary>Read-only decision; the calling FlowScript owns cleanup and field transition.</summary>
internal static class BossEntryPolicy
{
    public static bool ShouldReturn(PalaceLocation? activePalace, int requiredTier,
        Func<PalaceLocation, bool> hasItem, Func<PalaceLocation, bool> hasSentCard) =>
        activePalace is not null && activePalace.ProgressiveIndex == requiredTier &&
        (!hasItem(activePalace) || !hasSentCard(activePalace));
}
