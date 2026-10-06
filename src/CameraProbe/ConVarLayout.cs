namespace CameraProbe;

// ConVar compatibility is independent of the player schema in reference/.
// Each supported build must also pass a complete live registry traversal.
internal sealed record ConVarLayout(ulong InterfaceOffset)
{
    internal static ConVarLayout ForBuild(int build) => build switch
    {
        14185 or 14186 or 14188 or 14189 => new(3851888),
        _ => throw new InvalidOperationException($"Unsupported ConVar engine build {build}; supported: 14185, 14186, 14188, 14189.")
    };
}
