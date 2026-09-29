namespace CameraProbe;

internal static class ConVarRecovery
{
    internal static void ValidateIdentity(ConVarEntry original, ConVarEntry actual)
    {
        _ = ConVarPolicy.Mask(original.Name, original.Type);
        if (actual.Name != original.Name || actual.Address != original.Address || actual.Type != original.Type ||
            actual.NodeAddress != original.NodeAddress || actual.NamePointer != original.NamePointer)
            throw new InvalidOperationException("Saved ConVar identity no longer matches the active registry.");
    }

    internal static string[] RestoreFlags(IEnumerable<ConVarEntry> entries,
        Func<ConVarEntry, ulong> readValidated, Action<ConVarEntry, ulong> write)
    {
        var errors = new List<string>();
        foreach (var entry in entries.Reverse())
        {
            try
            {
                ulong current = readValidated(entry);
                ulong restored = ConVarPolicy.Restore(entry.Name, entry.Type, entry.Flags, current);
                if (current != restored) write(entry, restored);
                if (readValidated(entry) != restored) throw new InvalidOperationException("Restoration verification failed.");
            }
            catch (Exception ex) { errors.Add($"{entry.Name}: {ex.Message}"); }
        }
        return errors.ToArray();
    }
}
