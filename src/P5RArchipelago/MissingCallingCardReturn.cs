namespace P5RArchipelago;

/// <summary>Owns one denied-card return until the hub script clears the Palace session.</summary>
internal sealed class MissingCallingCardReturn(
    Func<PalaceLocation?> activePalace,
    Func<PalaceLocation, bool> hasCallingCardAccess,
    Action returnToHub,
    Log log)
{
    private int _pendingMajor;

    public bool TryReturn(PalaceLocation palace)
    {
        if (activePalace()?.Major != palace.Major)
            return false;
        if (_pendingMajor == palace.Major)
            return true;
        if (hasCallingCardAccess(palace))
            return false;

        _pendingMajor = palace.Major;
        try
        {
            returnToHub();
            log.Info($"Returning '{palace.Name}' to Mementos: its calling-card requirement is missing or its route is disabled.");
            return true;
        }
        catch (Exception ex)
        {
            _pendingMajor = 0;
            log.Error($"Could not queue the missing-calling-card return for '{palace.Name}': {ex.Message}");
            return false;
        }
    }

    public void Reset() => _pendingMajor = 0;
}
