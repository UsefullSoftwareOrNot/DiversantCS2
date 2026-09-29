namespace CameraProbe;

internal static class ConVarPolicy
{
    internal static ulong Mask(string name, short type) => (name, type) switch
    {
        ("mat_fullbright", 3) => 1UL << 14,
        ("spec_freeze_time", 7) => 1UL << 13,
        _ => throw new InvalidOperationException("Only mat_fullbright/int32 and spec_freeze_time/float32 are supported.")
    };
    internal static ulong Unlock(string name, short type, ulong flags) => flags & ~Mask(name, type);
    internal static ulong Restore(string name, short type, ulong original, ulong current)
    {
        ulong mask = Mask(name, type);
        return (current & ~mask) | (original & mask);
    }
}
