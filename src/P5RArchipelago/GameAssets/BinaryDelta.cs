using System.Buffers.Binary;
using System.IO.Compression;

namespace P5RArchipelago.GameAssets;

/// <summary>BSDIFF control/difference data wrapped in gzip, with no external decoder.</summary>
internal static class BinaryDelta
{
    public static byte[] Apply(byte[] original, byte[] patch)
    {
        if (patch.Length < 32 || !patch.AsSpan(0, 8).SequenceEqual("P5APDIF1"u8))
            throw new InvalidDataException("Invalid Persona 5 AP patch header.");
        var controlLength = BinaryPrimitives.ReadInt64LittleEndian(patch.AsSpan(8, 8));
        var diffLength = BinaryPrimitives.ReadInt64LittleEndian(patch.AsSpan(16, 8));
        var outputLength = BinaryPrimitives.ReadInt64LittleEndian(patch.AsSpan(24, 8));
        const int limit = 16 * 1024 * 1024;
        if (controlLength < 0 || controlLength > limit || controlLength % 24 != 0 ||
            diffLength < 0 || diffLength > limit || outputLength < 0 || outputLength > limit)
            throw new InvalidDataException("Invalid Persona 5 AP patch sizes.");
        using var compressed = new MemoryStream(patch, 32, patch.Length - 32);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var data = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = gzip.Read(buffer)) != 0)
        {
            if (data.Length + read > controlLength + diffLength + outputLength)
                throw new InvalidDataException("Oversized Persona 5 AP patch payload.");
            data.Write(buffer, 0, read);
        }
        var payload = data.ToArray();
        if (payload.Length < controlLength + diffLength)
            throw new InvalidDataException("Truncated Persona 5 AP patch payload.");
        var output = new byte[(int)outputLength];
        long oldPosition = 0;
        var newPosition = 0;
        var diffPosition = (int)controlLength;
        var extraPosition = checked((int)(controlLength + diffLength));
        for (var control = 0; control < controlLength; control += 24)
        {
            var add = SignedOffset(payload.AsSpan(control, 8));
            var copy = SignedOffset(payload.AsSpan(control + 8, 8));
            var seek = SignedOffset(payload.AsSpan(control + 16, 8));
            if (add < 0 || copy < 0 || add > output.Length - newPosition ||
                copy > output.Length - newPosition - add || add > controlLength + diffLength - diffPosition ||
                copy > payload.Length - extraPosition)
                throw new InvalidDataException("Invalid Persona 5 AP patch operation.");
            for (var i = 0; i < (int)add; i++)
            {
                var oldIndex = checked(oldPosition + i);
                var oldByte = oldIndex >= 0 && oldIndex < original.Length ? original[(int)oldIndex] : 0;
                output[newPosition++] = unchecked((byte)(payload[diffPosition++] + oldByte));
            }
            payload.AsSpan(extraPosition, (int)copy).CopyTo(output.AsSpan(newPosition));
            extraPosition += (int)copy;
            newPosition += (int)copy;
            oldPosition = checked(oldPosition + add + seek);
        }
        if (newPosition != output.Length || diffPosition != controlLength + diffLength || extraPosition != payload.Length)
            throw new InvalidDataException("Incomplete Persona 5 AP patch.");
        return output;
    }

    // BSDIFF offsets use sign/magnitude, rather than two's complement.
    private static long SignedOffset(ReadOnlySpan<byte> data)
    {
        var value = BinaryPrimitives.ReadUInt64LittleEndian(data);
        var magnitude = (long)(value & long.MaxValue);
        return (value & (1UL << 63)) == 0 ? magnitude : -magnitude;
    }
}
