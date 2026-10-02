namespace CameraProbe;
public static class Validation
{
    public static void Build(int expected, int actual)
    {
        if (expected <= 0 || expected != actual)
            throw new InvalidOperationException($"Detected engine build {actual}; player schema supports {expected}. Schema and recovery code must be verified for this build before memory writes can start.");
    }
    public static void Pointer(ulong address)
    {
        if (address < 0x10000 || address > 0x00007FFFFFFFFFFF)
            throw new InvalidOperationException($"Invalid user-mode pointer 0x{address:X}.");
    }
    public static void ReadLength(nuint actual, nuint expected)
    {
        if (actual != expected) throw new InvalidOperationException("Partial process memory read.");
    }
    public static void Vector(float[] vector)
    {
        if (vector.Length != 3 || vector.Any(v => !float.IsFinite(v)))
            throw new InvalidOperationException("Invalid camera vector.");
    }
}
public static class SwitchPolicy
{
    public static ushort[] Teams(int team, bool freeze, bool foreground, int delay)
    {
        if (!foreground) throw new InvalidOperationException("CS2 is not the foreground window.");
        if (!freeze) throw new InvalidOperationException("Team switching requires freeze time.");
        if (team is not (2 or 3)) throw new InvalidOperationException("Local player must be on T or CT.");
        if (delay is < 30 or > 1000) throw new InvalidOperationException("Delay must be 30..1000 ms.");
        return team == 3 ? [2, 3] : [3, 2];
    }
}
