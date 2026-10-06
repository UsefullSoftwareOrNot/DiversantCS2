using System.Text.Json;

namespace CameraProbe;

internal static class CompatibilityRequirements
{
    internal static readonly string[] Globals =
        ["dwLocalPlayerController", "dwGameRules", "dwGameEntitySystem", "dwViewMatrix", "dwViewAngles"];

    internal static readonly (string Type, string Field)[] Fields =
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

    internal static JsonElement Unique(JsonElement parent, string name)
    {
        JsonElement value = default;
        int count = 0;
        foreach (JsonProperty property in parent.EnumerateObject())
            if (property.NameEquals(name)) { value = property.Value; count++; }
        if (count != 1) throw new InvalidOperationException($"Compatibility property must occur exactly once: {name}.");
        return value;
    }
}
