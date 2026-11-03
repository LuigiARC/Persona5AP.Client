using System.Runtime.InteropServices;

namespace P5RArchipelago.Native;

/// <summary>
/// Opens the existing, game-native hub BF from a keyboard shortcut. RunFlowProcedure is the
/// same enqueueing entry point proven by Daphoa's earlier P5R Archipelago prototype; it does
/// not execute the flow interpreter on this polling thread.
/// </summary>
internal sealed class HubMenuHotkey : IDisposable
{
    private const string SequenceRootPattern =
        "48 8B 05 ?? ?? ?? ?? 48 8B 48 48 0F B6 01 24 01 C3";

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private readonly HubFlowRunner _hubFlow;
    private readonly IntPtr _sequenceRootAddress;
    private readonly int _virtualKey;
    private readonly Func<bool> _tryBegin;
    private readonly Action _cancelBegin;
    private readonly Func<bool> _allowWhenFieldProbeFails;
    private readonly Log _log;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _pollTask;

    public HubMenuHotkey(AddressScanner scanner, HubFlowRunner hubFlow, string keyName,
        Func<bool> tryBegin, Action cancelBegin, Func<bool> allowWhenFieldProbeFails, Log log)
    {
        _log = log;
        _hubFlow = hubFlow;
        _tryBegin = tryBegin;
        _cancelBegin = cancelBegin;
        _allowWhenFieldProbeFails = allowWhenFieldProbeFails;
        _virtualKey = ParseVirtualKey(keyName);

        var sequenceInstruction = scanner.FindUnique("SequenceRoot", SequenceRootPattern);
        _sequenceRootAddress = AddressScanner.ResolveRipRelative(sequenceInstruction, 3, 7);

        _pollTask = Task.Run(PollAsync);
        _log.Info($"Archipelago hub hotkey is {NormalizeKeyName(keyName)} (available after loading a save while the field controller is active).");
    }

    private async Task PollAsync()
    {
        var wasDown = false;
        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                var isDown = (GetAsyncKeyState(_virtualKey) & 0x8000) != 0;
                if (isDown && !wasDown)
                    TryOpenHub();
                wasDown = isDown;
                await Task.Delay(40, _cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"Archipelago hub hotkey polling stopped: {ex.Message}");
        }
    }

    private void TryOpenHub()
    {
        var fieldActive = IsFieldControllerActive();
        if (!fieldActive && !_allowWhenFieldProbeFails())
        {
            _log.Info("Ignored hub hotkey while the field controller was inactive.");
            return;
        }

        if (!_tryBegin())
        {
            _log.Info("Ignored hub hotkey before a save load or while the hub is already open.");
            return;
        }

        try
        {
            // Procedure30 is the native root with Connection, Shop and Warps submenus.
            _hubFlow.Run(30);
            _log.Info(fieldActive
                ? "Opened the Archipelago hotkey menu."
                : "Opened the Archipelago hotkey menu using the active-Palace fallback.");
        }
        catch (Exception ex)
        {
            _cancelBegin();
            _log.Error($"Could not open the Archipelago hub from the hotkey: {ex.Message}");
        }
    }

    internal bool IsFieldControllerActive()
    {
        try
        {
            var sequenceObject = Marshal.ReadIntPtr(_sequenceRootAddress);
            if (sequenceObject == IntPtr.Zero) return false;
            var sequenceInfo = Marshal.ReadIntPtr(sequenceObject + 0x48);
            // Mirror the resolved executable helper exactly: it reads the first byte of this
            // object and tests bit 0. The old code incorrectly read an int at +4 and compared
            // it with a guessed enum value (3), rejecting both Palace and Mementos fields.
            return sequenceInfo != IntPtr.Zero && (Marshal.ReadByte(sequenceInfo) & 1) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static int ParseVirtualKey(string keyName)
    {
        var normalized = NormalizeKeyName(keyName);
        if (normalized.Length is 2 or 3 && normalized[0] == 'F' &&
            int.TryParse(normalized.AsSpan(1), out var functionKey) && functionKey is >= 1 and <= 12)
            return 0x6F + functionKey;

        return normalized switch
        {
            "INSERT" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "PAUSE" => 0x13,
            _ => throw new InvalidOperationException(
                $"Unsupported HubMenuHotkey '{keyName}'. Use F1-F12, Insert, Home, End, PageUp, PageDown, Pause, or an empty string."),
        };
    }

    private static string NormalizeKeyName(string keyName) =>
        keyName.Trim().Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

    public void Dispose()
    {
        _cancellation.Cancel();
        try { _pollTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _cancellation.Dispose();
    }
}
