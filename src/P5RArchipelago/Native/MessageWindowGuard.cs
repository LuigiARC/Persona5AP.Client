using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

/// <summary>Defer a null AP message-window update at the exact helper in the captured
/// crash. True means native input/update is blocked; no nonexistent window is dereferenced.</summary>
internal sealed class MessageWindowGuard : IDisposable
{
    internal const string Pattern =
        "48 83 EC 28 48 83 79 70 00 75 42 48 8D 05 ?? ?? ?? ?? 48 8D 15 ?? ?? ?? ?? 0F 1F 80 00 00 00 00";
    [Function(CallingConventions.Microsoft)]
    private delegate byte WindowBlocked(IntPtr window);
    private readonly IHook<WindowBlocked> _hook;
    private readonly Func<bool> _apScope;
    private readonly Log _log;
    private int _reported;

    public MessageWindowGuard(IReloadedHooks hooks, AddressScanner scanner, Func<bool> apScope, Log log)
    {
        _apScope = apScope; _log = log;
        var address = scanner.FindUnique("MessageWindowBlocked", Pattern);
        _hook = hooks.CreateHook<WindowBlocked>(IsBlocked, address);
        _hook.Activate();
    }

    private byte IsBlocked(IntPtr window)
    {
        if (window == IntPtr.Zero && _apScope())
        {
            if (Interlocked.Exchange(ref _reported, 1) == 0)
                _log.Info("Deferred a null native message window during AP menu/Palace use (captured crash guard).");
            return 1;
        }
        return _hook.OriginalFunction(window);
    }

    public void Dispose() => _hook.Disable();
}
