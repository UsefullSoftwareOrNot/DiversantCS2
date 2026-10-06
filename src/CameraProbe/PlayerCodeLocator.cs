namespace CameraProbe;

internal static class PlayerCodeLocator
{
    private static readonly BytePattern CameraEntry = BytePattern.Parse(
        "48 8B 4F 38 48 8B 01 FF 90 E8 04 00 00 84 C0 75 6D");
    private static readonly BytePattern DeathLoad = BytePattern.Parse("F3 0F 10 80 ?? ?? ?? ??");
    private static readonly BytePattern CameraCompare = BytePattern.Parse(
        "E8 ?? ?? ?? ?? 0F 2F 05 ?? ?? ?? ?? 76 20 4D 8B CF 4D 8B C6 48 8B D6 48 8B CF E8 ?? ?? ?? ??");
    private static readonly BytePattern MovementHealth = BytePattern.Parse("44 39 B8 ?? ?? ?? ?? 7F 1E");

    internal static PlayerCodeLayout Locate(IEnumerable<(int Rva, byte[] Code)> sourceSections,
        int imageSize, int deathTimeOffset, int healthOffset)
    {
        var sections = sourceSections.ToArray();
        if (imageSize <= 0) throw new InvalidOperationException("Invalid client image size.");
        foreach (var (rva, code) in sections)
            if (rva < 0 || code.Length == 0 || (long)rva + code.Length > imageSize)
                throw new InvalidOperationException("Executable section is outside client.dll.");

        PatternMatch Find(string name, BytePattern pattern, IEnumerable<(int Rva, byte[] Code)> input)
        {
            try { return pattern.FindUnique(input); }
            catch (InvalidOperationException ex) { throw new InvalidOperationException($"Unable to locate {name} code.", ex); }
        }

        var cameraCandidates = new List<(PatternMatch Entry, PatternMatch Death, PatternMatch Compare)>();
        foreach (PatternMatch entryMatch in CameraEntry.FindAll(sections))
        {
            var owner = sections.Single(s => entryMatch.Rva >= s.Rva &&
                entryMatch.Rva < (long)s.Rva + s.Code.Length);
            int local = entryMatch.Rva - owner.Rva;
            int neighborhoodLength = Math.Min(0x100, owner.Code.Length - local);
            var neighborhood = new[]
                { (entryMatch.Rva, owner.Code.AsSpan(local, neighborhoodLength).ToArray()) };
            try
            {
                PatternMatch deathMatch = DeathLoad.FindUnique(neighborhood);
                if (BitConverter.ToInt32(deathMatch.Bytes, 4) != deathTimeOffset) continue;
                PatternMatch compareMatch = CameraCompare.FindUnique(neighborhood);
                ValidateRelativeTarget(compareMatch.Rva, compareMatch.Bytes, 0, 1, 5, imageSize);
                ValidateRelativeTarget(compareMatch.Rva, compareMatch.Bytes, 5, 8, 7, imageSize);
                ValidateRelativeTarget(compareMatch.Rva, compareMatch.Bytes, 26, 27, 5, imageSize);
                cameraCandidates.Add((entryMatch, deathMatch, compareMatch));
            }
            catch (InvalidOperationException) { }
        }
        if (cameraCandidates.Count != 1)
            throw new InvalidOperationException(cameraCandidates.Count == 0
                ? "Unable to locate a structurally valid camera code block."
                : "Camera code block is ambiguous.");
        var (entry, death, compare) = cameraCandidates[0];

        PatternMatch movement = Find("movement health-check", MovementHealth, sections);
        if (BitConverter.ToInt32(movement.Bytes, 3) != healthOffset)
            throw new InvalidOperationException("Movement health field displacement does not match the schema.");

        return new PlayerCodeLayout(
            (ulong)entry.Rva, Convert.ToHexString(entry.Bytes),
            (ulong)death.Rva, Convert.ToHexString(death.Bytes),
            (ulong)compare.Rva, Convert.ToHexString(compare.Bytes),
            (ulong)movement.Rva, Convert.ToHexString(movement.Bytes));
    }

    private static void ValidateRelativeTarget(int blockRva, byte[] bytes, int instructionOffset,
        int displacementOffset, int instructionLength, int imageSize)
    {
        int displacement = BitConverter.ToInt32(bytes, displacementOffset);
        long target = (long)blockRva + instructionOffset + instructionLength + displacement;
        if (target < 0 || target >= imageSize)
            throw new InvalidOperationException("Relative instruction target is outside client.dll.");
    }
}
