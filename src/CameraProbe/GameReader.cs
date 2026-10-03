using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace CameraProbe;

internal sealed class GameReader : IDisposable
{
    private readonly Process process;
    private readonly SafeProcessHandle handle = null!;
    private readonly JsonDocument schema = null!;
    private readonly JsonDocument offsets = null!;
    private readonly ulong client;
    private readonly int build;
    private readonly bool conVarsOnly;
    internal int ProcessId => process.Id;
    internal long StartTicks => process.StartTime.ToUniversalTime().Ticks;
    internal bool HasExited => process.HasExited;
    internal int Build => build;

    internal GameReader(string referenceDirectory, bool conVarsOnly = false)
    {
        this.conVarsOnly = conVarsOnly;
        var candidates = Process.GetProcessesByName("cs2");
        if (candidates.Length != 1)
        {
            foreach (var item in candidates) item.Dispose();
            throw new InvalidOperationException($"Expected one cs2.exe process; found {candidates.Length}.");
        }
        process = candidates[0];
        try
        {
            handle = Native.OpenProcess(0x0010 | 0x1000, false, process.Id); // READ + QUERY_LIMITED_INFORMATION
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open CS2 for reading");
            build = DiscoverEngineBuild();
            if (conVarsOnly) _ = ConVarLayout.ForBuild(build);
            else
            {
                ProcessModule clientModule = process.Modules.Cast<ProcessModule>().Single(m =>
                    m.ModuleName.Equals("client.dll", StringComparison.OrdinalIgnoreCase));
                string clientHash = DiscoverClientHash(clientModule);
                string profile = ReferenceProfile.Resolve(referenceDirectory, build, clientHash);
                schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(profile, "client_dll.json")));
                offsets = JsonDocument.Parse(File.ReadAllText(Path.Combine(profile, "offsets.json")));
                client = (ulong)clientModule.BaseAddress;
                using var info = JsonDocument.Parse(File.ReadAllText(Path.Combine(profile, "info.json")));
                Validation.Build(info.RootElement.GetProperty("build_number").GetInt32(), build);
            }
        }
        catch { Dispose(); throw; }
    }

    private int DiscoverEngineBuild()
    {
        ProcessModule engine = process.Modules.Cast<ProcessModule>().Single(m =>
            m.ModuleName.Equals("engine2.dll", StringComparison.OrdinalIgnoreCase));
        ulong address = (ulong)engine.BaseAddress;
        using var stream = File.Open(engine.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var pe = new PEReader(stream);
        var headers = pe.PEHeaders;
        if (headers.PEHeader?.Magic != PEMagic.PE32Plus || headers.PEHeader.SizeOfImage != engine.ModuleMemorySize)
            throw new InvalidOperationException("Loaded engine image does not match its PE file.");
        int peOffset = Read<int>(address + 0x3C);
        if (peOffset < 64 || peOffset > engine.ModuleMemorySize - 24 || Read<ushort>(address) != 0x5A4D ||
            Read<uint>(address + (ulong)peOffset) != 0x4550 ||
            Read<int>(address + (ulong)peOffset + 8) != headers.CoffHeader.TimeDateStamp)
            throw new InvalidOperationException("Engine file changed since it was loaded. Restart CS2 before compatibility discovery.");
        var sections = new List<(int Rva, byte[] Code)>();
        foreach (var section in headers.SectionHeaders)
        {
            if ((section.SectionCharacteristics & SectionCharacteristics.MemExecute) == 0) continue;
            if (section.VirtualAddress < 0 || section.VirtualSize <= 0 ||
                (long)section.VirtualAddress + section.VirtualSize > engine.ModuleMemorySize)
                throw new InvalidOperationException("Invalid executable engine section.");
            sections.Add((section.VirtualAddress, Bytes(address + (ulong)section.VirtualAddress, section.VirtualSize)));
        }
        int offset = EngineBuildLocator.FindOffset(sections, engine.ModuleMemorySize);
        int value = Read<int>(address + (ulong)offset);
        if (value <= 0) throw new InvalidOperationException("Discovered engine build value is invalid; memory writes are disabled.");
        return value;
    }

    private string DiscoverClientHash(ProcessModule module)
    {
        ulong address = (ulong)module.BaseAddress;
        using var stream = File.Open(module.FileName, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        int fileTimestamp, fileImageSize;
        using (var pe = new PEReader(stream, PEStreamOptions.LeaveOpen))
        {
            var headers = pe.PEHeaders;
            if (headers.PEHeader?.Magic != PEMagic.PE32Plus)
                throw new InvalidOperationException("Loaded client image is not a 64-bit PE file.");
            fileTimestamp = headers.CoffHeader.TimeDateStamp;
            fileImageSize = headers.PEHeader.SizeOfImage;
        }
        int peOffset = Read<int>(address + 0x3C);
        if (fileImageSize != module.ModuleMemorySize || peOffset < 64 || peOffset > module.ModuleMemorySize - 24 ||
            Read<ushort>(address) != 0x5A4D || Read<uint>(address + (ulong)peOffset) != 0x4550 ||
            Read<int>(address + (ulong)peOffset + 8) != fileTimestamp)
            throw new InvalidOperationException("Client file changed since it was loaded. Restart CS2 before compatibility discovery.");
        stream.Position = 0;
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal (ulong Address, int Size) Module(string name)
    {
        foreach (ProcessModule module in process.Modules)
            if (module.ModuleName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return ((ulong)module.BaseAddress, module.ModuleMemorySize);
        throw new InvalidOperationException($"{name} is not loaded.");
    }

    private int Offset(string module, string name) => offsets.RootElement.GetProperty(module).GetProperty(name).GetInt32();
    private int Field(string type, string field) => schema.RootElement.GetProperty("client.dll")
        .GetProperty("classes").GetProperty(type).GetProperty("fields").GetProperty(field).GetInt32();
    private T Value<T>(ulong owner, string type, string field) where T : unmanaged
    {
        Validation.Pointer(owner);
        return Read<T>(checked(owner + (ulong)Field(type, field)));
    }
    private T Global<T>(string name) where T : unmanaged => Read<T>(client + (ulong)Offset("client.dll", name));
    internal byte[] Bytes(ulong address, int size)
    {
        Validation.Pointer(address);
        Validation.Pointer(checked(address + (ulong)size - 1));
        byte[] buffer = new byte[size];
        if (!Native.ReadProcessMemory(handle, (nint)address, buffer, (nuint)size, out var count))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CS2 state unavailable during memory read");
        Validation.ReadLength(count, (nuint)size);
        return buffer;
    }
    internal T Read<T>(ulong address) where T : unmanaged => MemoryMarshal.Read<T>(Bytes(address, Marshal.SizeOf<T>()));
    private float[] Vector(ulong owner, string type, string field)
    {
        Validation.Pointer(owner);
        var bytes = Bytes(owner + (ulong)Field(type, field), 12);
        float[] values = [BitConverter.ToSingle(bytes, 0), BitConverter.ToSingle(bytes, 4), BitConverter.ToSingle(bytes, 8)];
        Validation.Vector(values);
        return values;
    }
    private string? DesignerName(ulong entity)
    {
        var identity = Value<ulong>(entity, "CEntityInstance", "m_pEntity");
        var namePointer = Value<ulong>(identity, "CEntityIdentity", "m_designerName");
        if (namePointer == 0) return null;
        var bytes = Bytes(namePointer, 64);
        int length = Array.IndexOf(bytes, (byte)0);
        return Encoding.ASCII.GetString(bytes, 0, length < 0 ? bytes.Length : length);
    }

    private string RuntimeClass(ulong entity)
    {
        var module = Module("client.dll");
        void InModule(ulong address, int length)
        {
            if (address < module.Address || address - module.Address > (ulong)(module.Size - length))
                throw new InvalidOperationException("Runtime type metadata is outside client.dll.");
        }
        ulong vtable = Read<ulong>(entity);
        InModule(vtable - 8, 8);
        ulong locator = Read<ulong>(vtable - 8);
        InModule(locator, 24);
        if (Read<uint>(locator) != 1 || module.Address + Read<uint>(locator + 20) != locator)
            throw new InvalidOperationException("Unsupported runtime type metadata.");
        ulong descriptor = module.Address + Read<uint>(locator + 12);
        InModule(descriptor, 112);
        byte[] name = Bytes(descriptor + 16, 96);
        int end = Array.IndexOf(name, (byte)0);
        if (end < 0) throw new InvalidOperationException("Unterminated runtime class name.");
        return Encoding.ASCII.GetString(name, 0, end);
    }

    internal Snapshot Capture() => CaptureCore(true);
    internal Snapshot CaptureSwitchState() => CaptureCore(false);

    internal object CaptureMovement()
    {
        var state = Capture();
        ulong pawn = ResolveLocalPawn(state.ControllerPawn ?? uint.MaxValue);
        if (pawn == 0 || state.PawnRuntimeClass != ".?AVC_CSPlayerPawn@@")
            throw new InvalidOperationException("No validated player pawn for movement capture.");
        ulong movement = Value<ulong>(pawn, "C_BasePlayerPawn", "m_pMovementServices");
        string movementClass = RuntimeClass(movement);
        if (movementClass != ".?AVCCSPlayer_MovementServices@@")
            throw new InvalidOperationException($"Unexpected movement service: {movementClass}");
        // CInButtonState is 32 bytes: vptr then three uint64 button-state planes.
        ulong buttons = movement + (ulong)Field("CPlayer_MovementServices", "m_nButtons") + 8;
        ulong scene = Value<ulong>(pawn, "C_BaseEntity", "m_pGameSceneNode");
        ulong modern = movement + (ulong)Field("CCSPlayer_MovementServices", "m_ModernJump");
        var result = new
        {
            State = state,
            Foreground = Native.IsForeground(ProcessId),
            SpaceDown = (Native.GetAsyncKeyState(0x20) & 0x8000) != 0,
            ForwardKeyDown = (Native.GetAsyncKeyState(0x57) & 0x8000) != 0,
            LeftKeyDown = (Native.GetAsyncKeyState(0x41) & 0x8000) != 0,
            BackKeyDown = (Native.GetAsyncKeyState(0x53) & 0x8000) != 0,
            RightKeyDown = (Native.GetAsyncKeyState(0x44) & 0x8000) != 0,
            MovementClass = movementClass,
            Buttons = new[] { Read<ulong>(buttons), Read<ulong>(buttons + 8), Read<ulong>(buttons + 16) },
            Flags = Value<uint>(pawn, "C_BaseEntity", "m_fFlags"),
            Ground = Value<uint>(pawn, "C_BaseEntity", "m_hGroundEntity"),
            MoveType = Value<byte>(pawn, "C_BaseEntity", "m_MoveType"),
            Position = Vector(scene, "CGameSceneNode", "m_vecAbsOrigin"),
            Velocity = Vector(pawn, "C_BaseEntity", "m_vecAbsVelocity"),
            ServerVelocity = Vector(pawn, "C_BaseEntity", "m_vecServerVelocity"),
            LastCommand = Value<uint>(movement, "CPlayer_MovementServices", "m_nLastCommandNumberProcessed"),
            ForwardMove = Value<float>(movement, "CPlayer_MovementServices", "m_flCmdForwardMove"),
            LastJumpTick = Value<int>(movement, "CCSPlayer_MovementServices", "m_nLastJumpTick"),
            LastActualJumpPressTick = Value<int>(modern, "CCSPlayerModernJump", "m_nLastActualJumpPressTick"),
            LastUsableJumpPressTick = Value<int>(modern, "CCSPlayerModernJump", "m_nLastUsableJumpPressTick"),
            LastLandedTick = Value<int>(modern, "CCSPlayerModernJump", "m_nLastLandedTick")
        };
        if (ResolveLocalPawn(state.ControllerPawn!.Value) != pawn ||
            Value<ulong>(pawn, "C_BasePlayerPawn", "m_pMovementServices") != movement)
            throw new InvalidOperationException("Pawn changed during movement capture.");
        return result;
    }

    private Snapshot CaptureCore(bool includeCamera)
    {
        if (conVarsOnly) throw new InvalidOperationException("Player capture requires a validated player schema.");
        if (process.HasExited) throw new InvalidOperationException("CS2 has exited.");
        var state = new Snapshot { Utc = DateTimeOffset.UtcNow, EngineBuild = build, ProcessId = process.Id };
        ulong controller = Global<ulong>("dwLocalPlayerController");
        ulong rules = Global<ulong>("dwGameRules");
        if (controller == 0) { state.Status = "no-local-controller"; return state; }
        if (DesignerName(controller) != "cs_player_controller")
            throw new InvalidOperationException("Controller class does not match schema; refusing to interpret memory.");
        if (Value<byte>(controller, "CBasePlayerController", "m_bIsLocalPlayerController") != 1)
            throw new InvalidOperationException("Controller is not the local player.");
        state.Team = Value<byte>(controller, "C_BaseEntity", "m_iTeamNum");
        if (state.Team > 3) throw new InvalidOperationException("Invalid local team value.");
        state.ControllerPawn = Value<uint>(controller, "CBasePlayerController", "m_hPawn");
        state.PlayerPawn = Value<uint>(controller, "CCSPlayerController", "m_hPlayerPawn");
        state.ObserverPawn = Value<uint>(controller, "CCSPlayerController", "m_hObserverPawn");
        state.PawnIsAlive = ReadBool(controller, "CCSPlayerController", "m_bPawnIsAlive");
        if (rules != 0)
        {
            state.FreezeTime = ReadBool(rules, "C_CSGameRules", "m_bFreezePeriod");
            state.RoundStartCount = Value<int>(rules, "C_CSGameRules", "m_nRoundStartCount");
        }
        if (includeCamera) Optional(state, () => CaptureCamera(state, controller));
        // Validate only the fields required for switching. Camera reads may fail independently.
        if (Global<ulong>("dwLocalPlayerController") != controller || Global<ulong>("dwGameRules") != rules ||
            Value<uint>(controller, "CBasePlayerController", "m_hPawn") != state.ControllerPawn ||
            Value<byte>(controller, "C_BaseEntity", "m_iTeamNum") != state.Team ||
            (rules != 0 && (ReadBool(rules, "C_CSGameRules", "m_bFreezePeriod") != state.FreezeTime ||
                Value<int>(rules, "C_CSGameRules", "m_nRoundStartCount") != state.RoundStartCount)))
            throw new InvalidOperationException("Player state changed during capture; sample discarded.");
        state.Status = includeCamera ? (state.Warnings.Count == 0 ? "sampled" : "partial-camera-sample") : "switch-state";
        return state;
    }

    private ulong ResolveLocalPawn(uint handle) => LocalPawnResolver.Resolve(Global<ulong>("dwGameEntitySystem"),
        handle, Read<ulong>, Read<uint>, Field("CEntityInstance", "m_pEntity"));

    internal (Snapshot State, ulong Pawn, ulong DeathTimeAddress) CameraExperimentTarget(CameraRecoveryState? recovery = null)
    {
        var state = Capture();
        if (recovery is not null) recovery.Require(state);
        else CameraExperimentPolicy.RequireBug(state);
        ulong pawn = ResolveLocalPawn(state.ControllerPawn!.Value);
        if (pawn == 0 || RuntimeClass(pawn) != ".?AVC_CSPlayerPawn@@")
            throw new InvalidOperationException("Camera experiment pawn changed.");
        return (state, pawn, pawn + (ulong)Field("C_BasePlayerPawn", "m_flDeathTime"));
    }

    internal (Snapshot State, ulong Pawn, ulong HealthAddress) MovementExperimentTarget()
    {
        var state = Capture();
        MovementExperimentPolicy.Require(state);
        ulong pawn = ResolveLocalPawn(state.ControllerPawn!.Value);
        if (pawn == 0 || RuntimeClass(pawn) != ".?AVC_CSPlayerPawn@@")
            throw new InvalidOperationException("Movement target changed.");
        return (state, pawn, pawn + (ulong)Field("C_BaseEntity", "m_iHealth"));
    }

    internal void VerifyMovementExperimentCode()
    {
        VerifyCameraExperimentCode();
        var layout = PlayerCodeLayout.ForBuild(build);
        // Explicit health comparison in this build: cmp dword ptr [rax+0x34c],r15d.
        byte[] expected = Convert.FromHexString(layout.MovementHealthHex);
        if (Field("C_BaseEntity", "m_iHealth") != 844 ||
            !Bytes(client + layout.MovementHealthRva, expected.Length).SequenceEqual(expected))
            throw new InvalidOperationException($"Movement health-check bytes do not match inspected build {build}.");
    }

    internal bool MatchesCameraPawn(uint entityHandle, ulong pawn)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return LocalPawnResolver.Resolve(Global<ulong>("dwGameEntitySystem"), entityHandle,
                    Read<ulong>, Read<uint>, Field("CEntityInstance", "m_pEntity"), retiredIsMissing: true) == pawn;
            }
            catch (Exception ex) when (attempt < 9 && !HasExited && ex is InvalidOperationException or Win32Exception)
            { Thread.Sleep(20); }
        }
    }

    internal void VerifyCameraExperimentCode()
    {
        bool Matches(ulong rva, string hex)
        {
            byte[] expected = Convert.FromHexString(hex);
            return Bytes(client + rva, expected.Length).SequenceEqual(expected);
        }
        var layout = PlayerCodeLayout.ForBuild(build);
        if (Field("C_BasePlayerPawn", "m_flDeathTime") != 5208 ||
            !Matches(layout.CameraEntryRva, layout.CameraEntryHex) ||
            !Matches(layout.CameraDeathLoadRva, layout.CameraDeathLoadHex) ||
            !Matches(layout.CameraCompareRva, layout.CameraCompareHex))
            throw new InvalidOperationException("Camera code does not match the inspected build; experiment refused.");
    }

    private void CaptureCamera(Snapshot state, ulong controller)
    {
        Optional(state, () =>
        {
            byte[] matrix = Bytes(client + (ulong)Offset("client.dll", "dwViewMatrix"), 64);
            float x = BitConverter.ToSingle(matrix, 48), y = BitConverter.ToSingle(matrix, 52), z = BitConverter.ToSingle(matrix, 56);
            Validation.Vector([x, y, z]);
            double horizontal = Math.Sqrt((double)x * x + (double)y * y);
            if (horizontal * horizontal + (double)z * z < 0.01) throw new InvalidOperationException("Degenerate view projection direction.");
            state.RenderedViewAngles = [(float)(Math.Atan2(-z, horizontal) * 180 / Math.PI), (float)(Math.Atan2(y, x) * 180 / Math.PI), 0];
        });
        Optional(state, () =>
        {
            byte[] angles = Bytes(client + (ulong)Offset("client.dll", "dwViewAngles"), 12);
            float[] vector = [BitConverter.ToSingle(angles, 0), BitConverter.ToSingle(angles, 4), BitConverter.ToSingle(angles, 8)];
            Validation.Vector(vector);
            state.InputViewAngles = vector;
        });
        // Prediction's global pawn is not necessarily the active observer pawn after a team switch.
        ulong pawn = ResolveLocalPawn(state.ControllerPawn ?? uint.MaxValue);
        if (pawn != 0)
        {
            state.PawnClass = DesignerName(pawn);
            state.PawnRuntimeClass = RuntimeClass(pawn);
            if (state.PawnRuntimeClass is not (".?AVC_CSPlayerPawn@@" or ".?AVC_CSObserverPawn@@"))
                throw new InvalidOperationException($"Unexpected local pawn runtime class: {state.PawnRuntimeClass} ({state.PawnClass}).");
        }
        if (pawn != 0)
        {
            ulong controllerIdentity = Value<ulong>(controller, "CEntityInstance", "m_pEntity");
            if (Value<uint>(pawn, "C_BasePlayerPawn", "m_hController") != Read<uint>(controllerIdentity + 0x10))
                throw new InvalidOperationException("Resolved pawn does not belong to the local controller.");
            state.Health = Value<int>(pawn, "C_BaseEntity", "m_iHealth");
            state.LifeState = Value<byte>(pawn, "C_BaseEntity", "m_lifeState");
            state.PawnTeam = Value<byte>(pawn, "C_BaseEntity", "m_iTeamNum");
            Optional(state, () => state.DeathTime = Value<float>(pawn, "C_BasePlayerPawn", "m_flDeathTime"));
            Optional(state, () => state.PawnViewAngles = Vector(pawn, "C_BasePlayerPawn", "v_angle"));
            Optional(state, () => state.LastCameraOrigin = Vector(pawn, "C_BasePlayerPawn", "m_vecLastCameraSetupLocalOrigin"));
            Optional(state, () =>
            {
                float time = Value<float>(pawn, "C_BasePlayerPawn", "m_flLastCameraSetupTime");
                if (!float.IsFinite(time)) throw new InvalidOperationException("Non-finite camera timestamp.");
                state.LastCameraTime = time;
            });
            Optional(state, () =>
            {
                ulong camera = Value<ulong>(pawn, "C_BasePlayerPawn", "m_pCameraServices");
                if (camera != 0) state.CameraViewEntity = Value<uint>(camera, "CPlayer_CameraServices", "m_hViewEntity");
            });
            Optional(state, () =>
            {
                ulong observer = Value<ulong>(pawn, "C_BasePlayerPawn", "m_pObserverServices");
                if (observer == 0) return;
                state.ObserverMode = Value<byte>(observer, "CPlayer_ObserverServices", "m_iObserverMode");
                state.ObserverTarget = Value<uint>(observer, "CPlayer_ObserverServices", "m_hObserverTarget");
                state.ForcedObserverMode = ReadBool(observer, "CPlayer_ObserverServices", "m_bForcedObserverMode");
                state.ObserverLastMode = Value<int>(observer, "CPlayer_ObserverServices", "m_iObserverLastMode");
            });
        }
        if (ResolveLocalPawn(state.ControllerPawn ?? uint.MaxValue) != pawn)
            throw new InvalidOperationException("Local pawn changed during camera capture; camera fields may be inconsistent.");
        if (pawn == 0) state.Warnings.Add("Local pawn is absent; its camera fields were not interpreted.");
    }
    private bool ReadBool(ulong entity, string type, string field)
    {
        byte value = Value<byte>(entity, type, field);
        if (value > 1) throw new InvalidOperationException($"Invalid boolean {field}.");
        return value == 1;
    }
    internal static void Optional(Snapshot state, Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or OverflowException)
        { state.Warnings.Add(ex.Message); }
    }
    public void Dispose() { handle?.Dispose(); process.Dispose(); schema?.Dispose(); offsets?.Dispose(); }
}

internal sealed class Snapshot
{
    public DateTimeOffset Utc { get; set; }
    public int ProcessId { get; set; }
    public int EngineBuild { get; set; }
    public string Status { get; set; } = "unknown";
    public byte? Team { get; set; }
    public bool? FreezeTime { get; set; }
    public int? RoundStartCount { get; set; }
    public bool? PawnIsAlive { get; set; }
    public uint? ControllerPawn { get; set; }
    public uint? PlayerPawn { get; set; }
    public uint? ObserverPawn { get; set; }
    public string? PawnClass { get; set; }
    public string? PawnRuntimeClass { get; set; }
    public int? Health { get; set; }
    public byte? LifeState { get; set; }
    public byte? PawnTeam { get; set; }
    public float? DeathTime { get; set; }
    public uint? CameraViewEntity { get; set; }
    public byte? ObserverMode { get; set; }
    public int? ObserverLastMode { get; set; }
    public bool? ForcedObserverMode { get; set; }
    public float[]? InputViewAngles { get; set; }
    public float[]? RenderedViewAngles { get; set; }
    public float[]? PawnViewAngles { get; set; }
    public uint? ObserverTarget { get; set; }
    public float[]? LastCameraOrigin { get; set; }
    public float? LastCameraTime { get; set; }
    public List<string> Warnings { get; set; } = [];
}
