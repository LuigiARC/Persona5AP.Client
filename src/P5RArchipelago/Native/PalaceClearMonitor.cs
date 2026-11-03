using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X86;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

internal sealed class PalaceClearMonitor
{
    private const string BitOnPattern =
        "40 53 48 83 EC 20 31 C9 E8 ?? ?? ?? ?? B9 01 00 00 00";
    private const string GetFlowIntArgumentPattern =
        "4C 8B 05 ?? ?? ?? ?? 41 8B 50 ?? 29 CA";

    [Function(CallingConventions.Fastcall)]
    private delegate uint BitOn();

    [Function(CallingConventions.Fastcall)]
    private delegate int GetFlowIntArgument(byte argumentIndex);

    private readonly IHook<BitOn> _bitOnHook;
    private readonly GetFlowIntArgument _getArgument;
    private readonly PalaceCatalog _catalog;
    private readonly Func<PalaceLocation, bool> _maySetCallingCard;
    private readonly Func<PalaceLocation, bool> _mayReportClear;
    private readonly Log _log;

    public event Action<PalaceLocation>? PalaceCleared;
    public event Action<PalaceLocation>? CallingCardSent;

    public PalaceClearMonitor(IReloadedHooks hooks, AddressScanner scanner, PalaceCatalog catalog,
        Func<PalaceLocation, bool> maySetCallingCard,
        Func<PalaceLocation, bool> mayReportClear,
        Log log)
    {
        _catalog = catalog;
        _maySetCallingCard = maySetCallingCard;
        _mayReportClear = mayReportClear;
        _log = log;
        var getArgumentAddress = scanner.FindUnique("GetFlowscriptInt4Arg", GetFlowIntArgumentPattern);
        _getArgument = hooks.CreateWrapper<GetFlowIntArgument>(getArgumentAddress, out _);
        var bitOnAddress = scanner.FindUnique("BIT_ON", BitOnPattern);
        _bitOnHook = hooks.CreateHook<BitOn>(BitOnImpl, bitOnAddress).Activate();
    }

    private uint BitOnImpl()
    {
        uint flag;
        try
        {
            flag = unchecked((uint)_getArgument(0));
        }
        catch (Exception ex)
        {
            _log.Error($"Could not inspect BIT_ON argument: {ex.Message}");
            return _bitOnHook.OriginalFunction();
        }

        // Several Palace field scripts set their story calling-card bit when the route is
        // secured. In AP those scripts can run months away from their vanilla calendar and
        // must not bypass the Progressive Calling Card item. Blocking the actual BIT_ON also
        // keeps the shared raid/boss state from being entered accidentally.
        if (_catalog.TryGetByCallingCardFlag(flag, out var palace) && !_maySetCallingCard(palace))
        {
            _log.Info($"Suppressed vanilla calling-card flag for '{palace.Name}'; its AP item has not been received.");
            return 0;
        }

        var result = _bitOnHook.OriginalFunction();
        if (_catalog.TryGetByClearFlag(flag, out palace))
        {
            if (_mayReportClear(palace))
                PalaceCleared?.Invoke(palace);
        }
        else if (_catalog.TryGetByCallingCardFlag(flag, out palace))
        {
            CallingCardSent?.Invoke(palace);
        }
        return result;
    }
}
