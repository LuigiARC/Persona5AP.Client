using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace P5RArchipelago.Native;

/// <summary>Keyboard input for our native generic selector only. The original input routine
/// remains untouched for every other selector. All editor/native calls run on the game thread.</summary>
internal sealed class ConnectionKeyboard : IDisposable
{
    [Function(CallingConventions.Microsoft)]
    private delegate byte SelectInput(IntPtr context);
    [Function(CallingConventions.Microsoft)]
    private delegate void SetMessageVariable(int window, int variable, int item, int type);
    [Function(CallingConventions.Microsoft)]
    private delegate ulong ActionInput(IntPtr query, int controller, ushort mask);
    [Function(CallingConventions.Microsoft)]
    private delegate IntPtr GetField();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyEx(uint key, uint type, IntPtr layout);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint key, uint scan, byte[] state, [Out] char[] text,
        int capacity, uint flags, IntPtr layout);

    private readonly IHook<SelectInput> _input;
    private readonly IHook<ActionInput> _actions;
    private readonly GetField _getField;
    private readonly SetMessageVariable _setVariable;
    private readonly Action<char> _append;
    private readonly Action _backspace;
    private readonly Action _clear;
    private readonly Func<bool> _ownsMenu;
    private readonly byte[] _keys = new byte[256];
    private readonly bool[] _previous = new bool[256];
    private readonly long[] _repeatAt = new long[256];
    private readonly char[] _text = new char[8];
    private int _header = -1;
    private bool _primed;
    private int _captureGameplay;
    private IntPtr _lockedField;
    private long _previousLocks;
    private const long FieldLockMask = 0xC00000000; // Native pad and movement locks, bits34/35.

    public ConnectionKeyboard(IReloadedHooks hooks, AddressScanner scanner,
        Action<char> append, Action backspace, Action clear, Func<bool> ownsMenu)
    {
        _append = append; _backspace = backspace; _clear = clear;
        _ownsMenu = ownsMenu;
        // Verified native generic input1415F6980, not a global game-keyboard hook.
        var input = scanner.FindUnique("GenericSelectorInput",
            "40 57 48 81 EC A0 00 00 00 48 8B F9 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 85 C0 74 15 48 8B 40 48");
        var setter = scanner.FindUnique("SetMessageVariable",
            "48 89 5C 24 10 48 89 6C 24 18 48 89 74 24 20 57 48 83 EC 30 48 8D 35 ?? ?? ?? ?? 49 63 C1 48 63 FA 48 63 D9");
        var actions = scanner.FindUnique("GameActionInput",
            "48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 48 89 7C 24 20 41 56 48 83 EC 20 41 0F B7 F8 8B F2 48 8B E9 66 45 85 C0");
        var padLock = scanner.FindUnique("FieldPadLock",
            "48 83 EC 28 E8 ?? ?? ?? ?? 48 85 C0 0F 84 BA 00 00 00 48 89 5C 24 30 48 8B 58 48 48 85 DB");
        // The real FLD_SET_PAD_LOCK gets its field owner through this rel32 call.
        var getter = padLock + 9 + Marshal.ReadInt32(padLock + 5);
        _getField = hooks.CreateWrapper<GetField>(getter, out _);
        _setVariable = hooks.CreateWrapper<SetMessageVariable>(setter, out _);
        _actions = hooks.CreateHook<ActionInput>(QueryAction, actions);
        _input = hooks.CreateHook<SelectInput>(Input, input);
        try { _actions.Activate(); _input.Activate(); }
        catch { _input.Disable(); _actions.Disable(); throw; }
    }

    public bool Begin(int header)
    {
        End();
        var owner = _getField();
        var field = owner == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(owner + 0x48);
        if (field == IntPtr.Zero) return false;
        _previousLocks = Marshal.ReadInt64(field + 0x1E0) & FieldLockMask;
        _lockedField = field;
        _header = header; _primed = false;
        Volatile.Write(ref _captureGameplay, 1);
        return true;
    }

    public void End()
    {
        _header = -1; _primed = false;
        if (_lockedField != IntPtr.Zero)
        {
            var owner = _getField();
            // Never write stale field memory after a warp/load. Restore only these two
            // flags, preserving every unrelated bit and any lock present before typing.
            if (owner != IntPtr.Zero && Marshal.ReadIntPtr(owner + 0x48) == _lockedField)
            {
                var address = _lockedField + 0x1E0;
                var current = Marshal.ReadInt64(address);
                Marshal.WriteInt64(address, (current & ~FieldLockMask) | _previousLocks);
            }
            _lockedField = IntPtr.Zero;
        }
        Volatile.Write(ref _captureGameplay, 0);
    }

    private ulong QueryAction(IntPtr query, int controller, ushort mask) =>
        Volatile.Read(ref _captureGameplay) != 0 && _ownsMenu() ? 0UL :
            _actions.OriginalFunction(query, controller, mask);

    private byte Input(IntPtr context)
    {
        // Generic controller: +20 message window, +28 header, +38 visible row count,
        // +14 result. Its native owner closes the renderer when this callback returns1.
        if (_header < 0 || !_ownsMenu() || Marshal.ReadInt32(context + 0x28) != _header ||
            Marshal.ReadInt32(context + 0x38) != 2)
            return _input.OriginalFunction(context);
        var window = Marshal.ReadInt32(context + 0x20);
        if (window is < 0 or >= 64) return _input.OriginalFunction(context);
        var foreground = GetForegroundWindow();
        var thread = GetWindowThreadProcessId(foreground, out var process);
        if (process != (uint)Environment.ProcessId) { _primed = false; return 0; }
        var layout = GetKeyboardLayout(thread);
        for (var key = 0; key < 256; key++)
            _keys[key] = (byte)((GetAsyncKeyState(key) & 0x8000) != 0 ? 0x80 : 0);
        _keys[0x14] |= (byte)(GetKeyState(0x14) & 1); // CapsLock toggle for ToUnicodeEx.
        var now = Environment.TickCount64;
        if (!_primed)
        {
            for (var key = 0; key < 256; key++) {
                _previous[key] = (_keys[key] & 0x80) != 0;
                _repeatAt[key] = now + 450;
            }
            _primed = true; // Do not consume the Enter that opened the editor.
        }
        else
        {
            for (var key = 0; key < 256; key++)
            {
                var down = (_keys[key] & 0x80) != 0;
                var pressed = down && !_previous[key];
                var repeat = down && _previous[key] && now >= _repeatAt[key];
                _previous[key] = down;
                if (pressed) _repeatAt[key] = now + 450;
                else if (repeat) _repeatAt[key] = now + 45;
                if (!pressed && !repeat) continue;
                if (pressed && key is 0x0D or 0x1B)
                {
                    Marshal.WriteInt32(context + 0x14, key == 0x0D ? 0 : -1);
                    // Mirror native confirm/cancel result consumed by the draw controller.
                    var renderer = Marshal.ReadIntPtr(context + 8);
                    var draw = Marshal.ReadIntPtr(renderer + 0x48);
                    Marshal.WriteInt64(draw + 0x930, key == 0x0D ? 0 : 1);
                    _header = -1;
                    return 1;
                }
                if (key == 8) { _backspace(); continue; }
                if (key == 0x2E) { if (pressed) _clear(); continue; }
                if (key < 0x20 || key is >= 0x70 and <= 0x87 ||
                    (_keys[0x11] & 0x80) != 0 || (_keys[0x12] & 0x80) != 0) continue;
                // Flag4 does not mutate the OS dead-key state. Printable ASCII matches
                // the existing connection protocol/editor; no passwords are logged.
                var count = ToUnicodeEx((uint)key, MapVirtualKeyEx((uint)key, 0, layout),
                    _keys, _text, _text.Length, 4, layout);
                for (var index = 0; index < Math.Min(count, _text.Length); index++)
                    if (_text[index] is >= ' ' and <= '~') _append(_text[index]);
                Array.Clear(_text);
            }
        }
        // The selector rebuilds header text each frame; update its native variable on
        // this same thread so the draft is visible immediately rather than on reopen.
        _setVariable(window, 0, 0x4FE4, 5);
        return 0; // Suppress native C/Space shortcuts while they are being typed.
    }

    public void Dispose() { End(); _input.Disable(); _actions.Disable(); }
}
