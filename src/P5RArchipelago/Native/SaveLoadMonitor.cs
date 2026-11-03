using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X86;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

internal sealed class SaveLoadMonitor
{
    private const string AppStorageReadPattern = "48 89 5C 24 ?? 57 48 83 EC 60 89 CB";

    [Function(CallingConventions.Fastcall)]
    private delegate IntPtr AppStorageRead(int first, uint fileIndex);

    private readonly IHook<AppStorageRead> _readHook;
    private readonly Log _log;

    public event Action<uint>? SaveLoaded;

    public SaveLoadMonitor(IReloadedHooks hooks, AddressScanner scanner, Log log)
    {
        _log = log;
        var address = scanner.FindUnique("AppStorageRead", AppStorageReadPattern);
        _readHook = hooks.CreateHook<AppStorageRead>(ReadImpl, address).Activate();
    }

    private IntPtr ReadImpl(int first, uint fileIndex)
    {
        var result = _readHook.OriginalFunction(first, fileIndex);
        if (fileIndex > 0)
        {
            _log.Info($"Save slot {fileIndex} loaded; reconciling persistent AP check state.");
            SaveLoaded?.Invoke(fileIndex);
        }
        return result;
    }
}
