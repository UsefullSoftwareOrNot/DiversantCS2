using System.Text.Json;

namespace CameraProbe;

internal static class ReferenceProfile
{
    internal static string Resolve(string root, int build, string clientSha256)
    {
        string hash = clientSha256.ToLowerInvariant();
        if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidOperationException("Invalid client.dll SHA-256 fingerprint.");
        string profile = Path.Combine(root, "builds",
            build.ToString(System.Globalization.CultureInfo.InvariantCulture), hash);
        if (Matches(profile, build, hash, allowLegacy14186: false)) return profile;
        if (Matches(root, build, hash, allowLegacy14186: build == 14186)) return root;
        throw new InvalidOperationException($"No verified player schema is installed for engine build {build} and client.dll {hash[..12]}.");
    }

    private static bool Matches(string directory, int build, string clientSha256, bool allowLegacy14186)
    {
        string infoPath = Path.Combine(directory, "info.json");
        string provenancePath = Path.Combine(directory, "provenance.json");
        if (!File.Exists(infoPath) || !File.Exists(provenancePath) ||
            !File.Exists(Path.Combine(directory, "client_dll.json")) ||
            !File.Exists(Path.Combine(directory, "offsets.json"))) return false;
        using var info = JsonDocument.Parse(File.ReadAllText(infoPath));
        using var provenance = JsonDocument.Parse(File.ReadAllText(provenancePath));
        if (info.RootElement.GetProperty("build_number").GetInt32() != build) return false;
        if (provenance.RootElement.TryGetProperty("client_sha256", out var recordedHash))
            return recordedHash.GetString()?.Equals(clientSha256, StringComparison.OrdinalIgnoreCase) == true;
        return allowLegacy14186;
    }
}
