using System.Diagnostics;
using System.Runtime.InteropServices;
using Reloaded.Memory.Sigscan;

namespace P5RArchipelago.Native;

internal sealed class AddressScanner
{
    private readonly IntPtr _baseAddress;
    private readonly int _moduleSize;
    private readonly Log _log;

    public AddressScanner(Log log)
    {
        _log = log;
        using var process = Process.GetCurrentProcess();
        var module = process.MainModule ?? throw new InvalidOperationException("P5R main module was not available.");
        _baseAddress = module.BaseAddress;
        _moduleSize = module.ModuleMemorySize;
    }

    public unsafe IntPtr FindUnique(string name, string pattern)
    {
        var scanner = new Scanner((byte*)_baseAddress, _moduleSize);
        var result = scanner.FindPattern(pattern);
        if (!result.Found)
            throw new InvalidOperationException($"Signature not found: {name}. This P5R executable is not yet supported.");

        var address = _baseAddress + result.Offset;
        _log.Info($"Resolved {name} at p5r.exe+0x{result.Offset:X}.");
        return address;
    }

    public static IntPtr ResolveRipRelative(IntPtr instruction, int displacementOffset, int instructionLength)
    {
        var displacement = Marshal.ReadInt32(instruction + displacementOffset);
        return instruction + instructionLength + displacement;
    }
}
