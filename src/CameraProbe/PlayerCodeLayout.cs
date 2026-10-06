namespace CameraProbe;

// Every entry is tied to instructions inspected in the matching client.dll.
// Field offsets alone are not enough to authorize the local recovery writes.
internal sealed record PlayerCodeLayout(
    ulong CameraEntryRva,
    string CameraEntryHex,
    ulong CameraDeathLoadRva,
    string CameraDeathLoadHex,
    ulong CameraCompareRva,
    string CameraCompareHex,
    ulong MovementHealthRva,
    string MovementHealthHex)
{
    internal static PlayerCodeLayout ForBuild(int build) => build switch
    {
        14186 => new(
            0x882F7C, "488B4F38488B01FF90E804000084C0756D",
            0x882FA7, "F30F108058140000",
            0x882FCC, "E89F8E8DFF0F2F058488380176204D8BCF4D8BC6488BD6488BCFE8D5F2FFFF",
            0x8C529E, "4439B84C0300007F1E"),
        14188 => new(
            0x8827BC, "488B4F38488B01FF90E804000084C0756D",
            0x8827E7, "F30F108058140000",
            0x88280C, "E82F9B8DFF0F2F05D492380176204D8BCF4D8BC6488BD6488BCFE8D5F2FFFF",
            0x8C4ADE, "4439B84C0300007F1E"),
        14189 => new(
            0x8827BC, "488B4F38488B01FF90E804000084C0756D",
            0x8827E7, "F30F108058140000",
            0x88280C, "E82F9B8DFF0F2F05D4B2380176204D8BCF4D8BC6488BD6488BCFE8D5F2FFFF",
            0x8C4ADE, "4439B84C0300007F1E"),
        _ => throw new InvalidOperationException($"Unsupported player-code build {build}; supported: 14186, 14188, 14189.")
    };
}
