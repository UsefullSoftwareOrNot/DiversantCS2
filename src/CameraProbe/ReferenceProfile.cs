using System.Text.Json;

namespace CameraProbe;

internal static class ReferenceProfile
{
    internal static string Resolve(string root, int build)
    {
        string profile = Path.Combine(root, "builds", build.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Matches(profile, build)) return profile;
        if (Matches(root, build)) return root;
        throw new InvalidOperationException($"No verified player schema is installed for engine build {build}.");
    }

    private static bool Matches(string directory, int build)
    {
        string path = Path.Combine(directory, "info.json");
        if (!File.Exists(path)) return false;
        using var info = JsonDocument.Parse(File.ReadAllText(path));
        return info.RootElement.GetProperty("build_number").GetInt32() == build;
    }
}
