namespace CameraProbe;

internal static class CompatibilityReport
{
    internal static string Format(ProfileSource source, int build, string clientSha256,
        string codeLayoutFingerprint) =>
        $"Compatibility: source={source.ToString().ToLowerInvariant()}; build={build}; " +
        $"client={Short(clientSha256)}; code={Short(codeLayoutFingerprint)}.";

    private static string Short(string value)
    {
        if (value.Length < 12) throw new InvalidOperationException("Compatibility fingerprint is incomplete.");
        return value[..12].ToLowerInvariant();
    }
}
