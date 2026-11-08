namespace P5RArchipelago.Native;

internal static class FieldRewardSafety
{
    // FLD_SET_PAD_LOCK / FLD_SET_MOVE_LOCK and FLD_SMP_EVENT_BEGIN / END.
    internal static bool IsReady(bool fieldPresent, long locks, ushort eventFlags) =>
        fieldPresent && (locks & 0xC00000000L) == 0 && (eventFlags & 1) == 0;
}
