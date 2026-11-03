using System.Runtime.InteropServices;

namespace P5RArchipelago.Native;

[StructLayout(LayoutKind.Explicit)]
internal unsafe struct FlowCommandData
{
    [FieldOffset(0x00)] public fixed char CurrentFunctionName[40];
    [FieldOffset(0x28)] public int CurrentInstructionIndex;
    [FieldOffset(0x2C)] public int StackSize;
    [FieldOffset(0x30)] public fixed byte ArgumentTypes[0x2F];
    [FieldOffset(0x5F)] public byte ReturnType;
    [FieldOffset(0x60)] public fixed long ArgumentData[0x2F];
    [FieldOffset(0x1D8)] public long ReturnValue;
}
