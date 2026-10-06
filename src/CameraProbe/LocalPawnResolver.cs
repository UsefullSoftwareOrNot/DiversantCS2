namespace CameraProbe;

internal static class LocalPawnResolver
{
    // Source 2 CEntitySystem/CConcreteEntityList/CEntityIdentity, checked on builds 14186, 14188 and 14189.
    internal static ulong Resolve(ulong system, uint handle, Func<ulong, ulong> readPointer,
        Func<ulong, uint> readUInt, int identityField, bool retiredIsMissing = false)
    {
        if (handle == uint.MaxValue) return 0;
        Validation.Pointer(system);
        uint index = handle & 0x7FFF;
        ulong chunkAddress = checked(system + 0x10 + (index >> 9) * 8UL);
        ulong chunk = readPointer(chunkAddress);
        if (chunk == 0) return 0;
        Validation.Pointer(chunk);
        ulong identity = checked(chunk + (index & 0x1FF) * 0x70UL);
        ulong pawn = readPointer(identity);
        if (pawn == 0) return 0;
        Validation.Pointer(pawn);
        uint serial = readUInt(identity + 0x10), flags = readUInt(identity + 0x30);
        if (serial != handle || (flags & 0x211) != 0)
        {
            if (retiredIsMissing && readPointer(chunkAddress) == chunk && readPointer(identity) == pawn &&
                readUInt(identity + 0x10) == serial && readUInt(identity + 0x30) == flags) return 0;
            throw new InvalidOperationException("Local pawn handle is stale or its identity changed.");
        }
        if (readPointer(pawn + (ulong)identityField) != identity)
            throw new InvalidOperationException("Local pawn handle is stale or its identity changed.");
        if (readPointer(chunkAddress) != chunk || readPointer(identity) != pawn || readUInt(identity + 0x10) != handle)
            throw new InvalidOperationException("Entity registry changed while resolving the local pawn.");
        return pawn;
    }
}
