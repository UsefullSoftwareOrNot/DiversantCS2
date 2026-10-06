using System.Text.Json;

namespace CameraProbe;

internal sealed class CompatibilityContext
{
    private static readonly string[] RequiredGlobals =
        ["dwLocalPlayerController", "dwGameRules", "dwGameEntitySystem", "dwViewMatrix", "dwViewAngles"];
    private static readonly (string Type, string Field)[] RequiredFields =
    [
        ("CEntityInstance", "m_pEntity"), ("CEntityIdentity", "m_designerName"),
        ("CBasePlayerController", "m_bIsLocalPlayerController"), ("CBasePlayerController", "m_hPawn"),
        ("CCSPlayerController", "m_hPlayerPawn"), ("CCSPlayerController", "m_hObserverPawn"),
        ("CCSPlayerController", "m_bPawnIsAlive"), ("C_BaseEntity", "m_iTeamNum"),
        ("C_BaseEntity", "m_pGameSceneNode"), ("C_BaseEntity", "m_fFlags"),
        ("C_BaseEntity", "m_hGroundEntity"), ("C_BaseEntity", "m_MoveType"),
        ("C_BaseEntity", "m_vecAbsVelocity"), ("C_BaseEntity", "m_vecServerVelocity"),
        ("C_BaseEntity", "m_iHealth"), ("C_BaseEntity", "m_lifeState"),
        ("CGameSceneNode", "m_vecAbsOrigin"), ("C_BasePlayerPawn", "m_pMovementServices"),
        ("C_BasePlayerPawn", "m_flDeathTime"), ("C_BasePlayerPawn", "m_hController"),
        ("C_BasePlayerPawn", "v_angle"), ("C_BasePlayerPawn", "m_vecLastCameraSetupLocalOrigin"),
        ("C_BasePlayerPawn", "m_flLastCameraSetupTime"), ("C_BasePlayerPawn", "m_pCameraServices"),
        ("C_BasePlayerPawn", "m_pObserverServices"), ("CPlayer_MovementServices", "m_nButtons"),
        ("CPlayer_MovementServices", "m_nLastCommandNumberProcessed"),
        ("CPlayer_MovementServices", "m_flCmdForwardMove"), ("CCSPlayer_MovementServices", "m_ModernJump"),
        ("CCSPlayer_MovementServices", "m_nLastJumpTick"),
        ("CCSPlayerModernJump", "m_nLastActualJumpPressTick"),
        ("CCSPlayerModernJump", "m_nLastUsableJumpPressTick"), ("CCSPlayerModernJump", "m_nLastLandedTick"),
        ("C_CSGameRules", "m_bFreezePeriod"), ("C_CSGameRules", "m_nRoundStartCount"),
        ("CPlayer_CameraServices", "m_hViewEntity"), ("CPlayer_ObserverServices", "m_iObserverMode"),
        ("CPlayer_ObserverServices", "m_hObserverTarget"),
        ("CPlayer_ObserverServices", "m_bForcedObserverMode"),
        ("CPlayer_ObserverServices", "m_iObserverLastMode")
    ];

    private readonly Dictionary<string, int> globals;
    private readonly Dictionary<string, int> fields;
    internal int Build { get; }
    internal string ClientSha256 { get; }
    internal ProfileSource Source { get; }
    internal string ProfilePath { get; }
    internal ulong ConVarInterfaceRva { get; }
    internal PlayerCodeLayout CodeLayout { get; }
    internal string CodeLayoutFingerprint => CodeLayout.Fingerprint;

    private CompatibilityContext(ResolvedProfile profile, int build, string clientSha256,
        Dictionary<string, int> globals, Dictionary<string, int> fields, ulong conVarInterfaceRva,
        PlayerCodeLayout codeLayout)
    {
        Build = build; ClientSha256 = clientSha256; Source = profile.Source; ProfilePath = profile.Path;
        this.globals = globals; this.fields = fields; ConVarInterfaceRva = conVarInterfaceRva; CodeLayout = codeLayout;
    }

    internal static CompatibilityContext Load(ResolvedProfile profile, int build, string clientSha256,
        int clientImageSize, IEnumerable<(int Rva, byte[] Code)> executableSections)
    {
        if (build <= 0 || clientImageSize <= 0) throw new InvalidOperationException("Invalid compatibility identity.");
        string hash = NormalizeHash(clientSha256);
        using var info = JsonDocument.Parse(File.ReadAllText(Path.Combine(profile.Path, "info.json")));
        if (info.RootElement.GetProperty("build_number").GetInt32() != build)
            throw new InvalidOperationException("Compatibility profile build does not match CS2.");
        using (var provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(profile.Path, "provenance.json"))))
        {
            if (provenance.RootElement.TryGetProperty("client_sha256", out var recorded))
            {
                if (!string.Equals(recorded.GetString(), hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Compatibility profile fingerprint does not match client.dll.");
            }
            else if (profile.Source != ProfileSource.Reviewed || build != 14186)
                throw new InvalidOperationException("Compatibility profile has no client.dll fingerprint.");
        }

        var globals = new Dictionary<string, int>(StringComparer.Ordinal);
        using (var offsets = JsonDocument.Parse(File.ReadAllText(Path.Combine(profile.Path, "offsets.json"))))
        {
            JsonElement module = offsets.RootElement.GetProperty("client.dll");
            foreach (string name in RequiredGlobals)
            {
                int value = module.GetProperty(name).GetInt32();
                if (value <= 0 || value >= clientImageSize)
                    throw new InvalidOperationException($"Client global is outside client.dll: {name}.");
                globals.Add(name, value);
            }
        }

        var fields = new Dictionary<string, int>(StringComparer.Ordinal);
        using (var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(profile.Path, "client_dll.json"))))
        {
            JsonElement classes = schema.RootElement.GetProperty("client.dll").GetProperty("classes");
            foreach (var (type, field) in RequiredFields)
            {
                int value = classes.GetProperty(type).GetProperty("fields").GetProperty(field).GetInt32();
                if (value < 0 || value >= 0x10000)
                    throw new InvalidOperationException($"Schema field is out of range: {type}.{field}.");
                fields.Add(Key(type, field), value);
            }
        }

        ulong conVar;
        using (var interfaces = JsonDocument.Parse(File.ReadAllText(Path.Combine(profile.Path, "interfaces.json"))))
        {
            long value = interfaces.RootElement.GetProperty("tier0.dll").GetProperty("VEngineCvar007").GetInt64();
            if (value <= 0 || value >= 0x40000000)
                throw new InvalidOperationException("VEngineCvar007 is out of range.");
            conVar = (ulong)value;
        }
        PlayerCodeLayout layout = PlayerCodeLocator.Locate(executableSections, clientImageSize,
            fields[Key("C_BasePlayerPawn", "m_flDeathTime")], fields[Key("C_BaseEntity", "m_iHealth")]);
        return new CompatibilityContext(profile, build, hash, globals, fields, conVar, layout);
    }

    internal int Global(string name) => globals.TryGetValue(name, out int value) ? value :
        throw new InvalidOperationException($"Unsupported client global: {name}.");
    internal int Field(string type, string field) => fields.TryGetValue(Key(type, field), out int value) ? value :
        throw new InvalidOperationException($"Unsupported schema field: {type}.{field}.");

    internal void RequireEquivalent(CompatibilityContext other)
    {
        if (Build != other.Build || ClientSha256 != other.ClientSha256 ||
            CodeLayoutFingerprint != other.CodeLayoutFingerprint || ConVarInterfaceRva != other.ConVarInterfaceRva ||
            !globals.OrderBy(p => p.Key).SequenceEqual(other.globals.OrderBy(p => p.Key)) ||
            !fields.OrderBy(p => p.Key).SequenceEqual(other.fields.OrderBy(p => p.Key)))
            throw new InvalidOperationException("Compatibility context changed.");
    }

    private static string Key(string type, string field) => type + "\0" + field;
    private static string NormalizeHash(string value)
    {
        string hash = value.ToLowerInvariant();
        if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidOperationException("Invalid client.dll SHA-256 fingerprint.");
        return hash;
    }
}
