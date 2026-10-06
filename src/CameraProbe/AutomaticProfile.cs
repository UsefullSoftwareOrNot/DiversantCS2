using System.Security.Cryptography;
using System.Text.Json;

namespace CameraProbe;

internal enum ProfileSource { Reviewed, Automatic }
internal sealed record ResolvedProfile(string Path, ProfileSource Source);

internal static class AutomaticProfile
{
    internal const string PinnedDumperSha256 = "501368ffb8f252b3cfb70fee6177b6ab4bbb21cad480724af17720d74ce44bbb";
    internal static ResolvedProfile Resolve(string cacheRoot, string toolPath, string expectedToolHash,
        int build, string clientSha256, Func<bool> identityUnchanged, DumperInvoker runner,
        int clientImageSize = 0x40000000, int tier0ImageSize = 0x40000000)
    {
        if (build <= 0) throw new InvalidOperationException("Invalid discovered engine build.");
        string hash = NormalizeHash(clientSha256, "client.dll");
        string expectedHash = NormalizeHash(expectedToolHash, "cs2-dumper");
        string fullCache = Path.GetFullPath(cacheRoot);
        string buildDirectory = Path.Combine(fullCache, build.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string final = Path.Combine(buildDirectory, hash);
        if (Directory.Exists(final))
        {
            try { Validate(final, build, hash, requireProvenance: true, clientImageSize, tier0ImageSize); return new(final, ProfileSource.Automatic); }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException or IOException)
            {
                Directory.CreateDirectory(buildDirectory);
                Directory.Move(final, Path.Combine(buildDirectory, $"{hash}.invalid-{Guid.NewGuid():N}"));
            }
        }

        if (!File.Exists(toolPath)) throw new InvalidOperationException("Bundled cs2-dumper is missing.");
        using (var stream = File.Open(toolPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Bundled cs2-dumper fingerprint does not match the pinned binary.");

        Directory.CreateDirectory(fullCache);
        string temporary = Path.Combine(fullCache, $".pending-{build}-{hash[..12]}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        DumperResult result = runner(toolPath, temporary, TimeSpan.FromSeconds(30));
        if (result.TimedOut) throw new InvalidOperationException("Bundled cs2-dumper timed out.");
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Bundled cs2-dumper failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        if (!identityUnchanged()) throw new InvalidOperationException("CS2 changed while automatic compatibility data was collected.");
        Validate(temporary, build, hash, requireProvenance: false, clientImageSize, tier0ImageSize);
        WriteProvenance(Path.Combine(temporary, "provenance.json"), build, hash, expectedHash);
        Validate(temporary, build, hash, requireProvenance: true, clientImageSize, tier0ImageSize);
        if (!identityUnchanged()) throw new InvalidOperationException("CS2 changed before compatibility data was published.");

        Directory.CreateDirectory(buildDirectory);
        try { Directory.Move(temporary, final); }
        catch (IOException) when (Directory.Exists(final))
        {
            Validate(final, build, hash, requireProvenance: true, clientImageSize, tier0ImageSize);
        }
        return new(final, ProfileSource.Automatic);
    }

    internal static void Validate(string directory, int build, string clientSha256, bool requireProvenance,
        int clientImageSize = 0x40000000, int tier0ImageSize = 0x40000000)
    {
        string[] required = ["info.json", "client_dll.json", "offsets.json", "interfaces.json"];
        if (required.Any(name => !File.Exists(Path.Combine(directory, name))))
            throw new InvalidOperationException("Automatic profile is incomplete.");
        using var info = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "info.json")));
        if (info.RootElement.GetProperty("build_number").GetInt32() != build)
            throw new InvalidOperationException("Automatic profile build does not match the running engine.");

        using var offsets = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "offsets.json")));
        JsonElement globals = CompatibilityRequirements.Unique(offsets.RootElement, "client.dll");
        foreach (string name in CompatibilityRequirements.Globals)
            Range(CompatibilityRequirements.Unique(globals, name).GetInt64(), $"client.dll.{name}",
                allowZero: false, clientImageSize);

        using var interfaces = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "interfaces.json")));
        long conVar = CompatibilityRequirements.Unique(
            CompatibilityRequirements.Unique(interfaces.RootElement, "tier0.dll"), "VEngineCvar007").GetInt64();
        if (conVar <= 0 || conVar > tier0ImageSize - 0x80L)
            throw new InvalidOperationException("Automatic profile value is out of range: tier0.dll.VEngineCvar007.");

        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "client_dll.json")));
        JsonElement classes = CompatibilityRequirements.Unique(
            CompatibilityRequirements.Unique(schema.RootElement, "client.dll"), "classes");
        foreach (var (type, field) in CompatibilityRequirements.Fields)
        {
            JsonElement fields = CompatibilityRequirements.Unique(
                CompatibilityRequirements.Unique(classes, type), "fields");
            Range(CompatibilityRequirements.Unique(fields, field).GetInt64(), $"{type}.{field}",
                allowZero: true, 0x10000);
        }

        if (!requireProvenance) return;
        using var provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "provenance.json")));
        if (provenance.RootElement.GetProperty("kind").GetString() != "automatic-local-discovery" ||
            provenance.RootElement.GetProperty("engine_build").GetInt32() != build ||
            !string.Equals(provenance.RootElement.GetProperty("client_sha256").GetString(), clientSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Automatic profile provenance does not match the running game.");
    }

    private static void Range(long value, string name, bool allowZero, long limit)
    {
        if (value < (allowZero ? 0 : 1) || value >= limit)
            throw new InvalidOperationException($"Automatic profile value is out of range: {name}.");
    }

    private static string NormalizeHash(string value, string name)
    {
        string hash = value.ToLowerInvariant();
        if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidOperationException($"Invalid {name} SHA-256 fingerprint.");
        return hash;
    }

    private static void WriteProvenance(string path, int build, string clientHash, string toolHash)
    {
        var value = new
        {
            kind = "automatic-local-discovery",
            engine_build = build,
            client_sha256 = clientHash,
            generated_utc = DateTimeOffset.UtcNow,
            tool = "a2x/cs2-dumper 0.1.3",
            binary_sha256 = toolHash
        };
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
            FileOptions.WriteThrough);
        JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
        stream.Flush(flushToDisk: true);
    }
}
