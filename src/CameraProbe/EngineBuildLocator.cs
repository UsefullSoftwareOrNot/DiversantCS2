using System.Buffers.Binary;

namespace CameraProbe;

internal static class EngineBuildLocator
{
    internal static int FindOffset(IEnumerable<(int Rva, byte[] Code)> sections, int imageSize)
    {
        // Build-number store pattern documented by a2x/cs2-dumper/src/analysis/offsets.rs.
        int? found = null;
        foreach (var (rva, code) in sections)
        {
            if (rva < 0 || (long)rva + code.Length > imageSize)
                throw new InvalidOperationException("Executable section exceeds the loaded engine image.");
            for (int i = 0; i <= code.Length - 22; i++)
            {
                var p = code.AsSpan(i, 22);
                if (p[0] != 0x89 || p[1] != 0x05 || p[6] != 0x48 || p[7] != 0x8D || p[8] != 0x0D ||
                    p[13] != 0xFF || p[14] != 0x15 || p[19] != 0x48 || p[20] != 0x8B || p[21] != 0x0D) continue;
                long target = (long)rva + i + 6 + BinaryPrimitives.ReadInt32LittleEndian(p.Slice(2, 4));
                if (target < 0 || target > (long)imageSize - 4)
                    throw new InvalidOperationException("Build-number signature points outside engine2.dll.");
                if (found.HasValue)
                    throw new InvalidOperationException("Engine build-number signature is ambiguous; refusing stale offsets.");
                found = (int)target;
            }
        }
        return found ?? throw new InvalidOperationException("Engine build-number signature was not found. Compatibility discovery requires an update.");
    }
}
