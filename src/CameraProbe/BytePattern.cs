namespace CameraProbe;

internal sealed record PatternMatch(int Rva, byte[] Bytes);

internal sealed class BytePattern
{
    private readonly byte[] values;
    private readonly bool[] exact;

    private BytePattern(byte[] values, bool[] exact)
    {
        this.values = values;
        this.exact = exact;
    }

    internal static BytePattern Parse(string pattern)
    {
        string[] tokens = pattern.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) throw new InvalidOperationException("Byte pattern is empty.");
        byte[] values = new byte[tokens.Length];
        bool[] exact = new bool[tokens.Length];
        for (int index = 0; index < tokens.Length; index++)
        {
            if (tokens[index] == "??") continue;
            if (tokens[index].Length != 2 || !byte.TryParse(tokens[index],
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out values[index]))
                throw new InvalidOperationException($"Invalid byte-pattern token: {tokens[index]}");
            exact[index] = true;
        }
        return new BytePattern(values, exact);
    }

    internal PatternMatch FindUnique(IEnumerable<(int Rva, byte[] Code)> sections)
    {
        PatternMatch[] matches = FindAll(sections).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("Byte pattern is ambiguous.");
        return matches.SingleOrDefault() ?? throw new InvalidOperationException("Byte pattern was not found.");
    }

    internal IEnumerable<PatternMatch> FindAll(IEnumerable<(int Rva, byte[] Code)> sections)
    {
        foreach (var (rva, code) in sections)
        {
            if (rva < 0 || code is null) throw new InvalidOperationException("Invalid code section.");
            for (int offset = 0; offset <= code.Length - values.Length; offset++)
            {
                bool matches = true;
                for (int index = 0; index < values.Length; index++)
                    if (exact[index] && code[offset + index] != values[index]) { matches = false; break; }
                if (!matches) continue;
                int address;
                try { address = checked(rva + offset); }
                catch (OverflowException ex) { throw new InvalidOperationException("Pattern address overflowed.", ex); }
                yield return new PatternMatch(address, code.AsSpan(offset, values.Length).ToArray());
            }
        }
    }
}
