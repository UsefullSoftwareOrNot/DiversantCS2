using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace CameraProbe;

internal sealed class CameraRecoveryState
{
    private (uint Handle, byte? Team, int? Round)? established;

    internal void RequireContinuation(Snapshot original, Snapshot current)
    {
        if (current.ControllerPawn != original.ControllerPawn || current.Team != original.Team ||
            current.RoundStartCount != original.RoundStartCount)
        {
            established = null;
            throw new InvalidOperationException("Camera recovery pawn, team or round changed.");
        }
        Require(current);
    }

    internal void Require(Snapshot state)
    {
        var identity = (state.ControllerPawn ?? uint.MaxValue, state.Team, state.RoundStartCount);
        bool continuing = established == identity;
        try { CameraExperimentPolicy.RequireBug(state, continuing); }
        catch { established = null; throw; }
        if (state.RoundStartCount is null)
        {
            established = null;
            throw new InvalidOperationException("Round identity is unavailable for camera recovery.");
        }
        established = identity;
    }
}

internal static class CameraExperimentPolicy
{
    internal const float TemporaryDeathTime = 1000000000f;
    internal static bool OwnsValue(float value) => value == TemporaryDeathTime;
    internal static bool ShouldRestore(float current, float original, bool applicationCompleted)
    {
        if (OwnsValue(current)) return true;
        if (applicationCompleted || current == original) return false;
        throw new InvalidOperationException("Uncertain interrupted camera write; journal retained. No unrelated value was overwritten. Restart CS2 before closing this journal.");
    }
    internal static void RequireBug(Snapshot state, bool continuing = false)
    {
        if (state.Team is not (2 or 3) || !(state.Health > 0 || (continuing && state.Health == 0)) || state.LifeState != 2 ||
            state.PawnIsAlive != false || state.ControllerPawn is null or uint.MaxValue ||
            state.ControllerPawn != state.PlayerPawn || state.PawnRuntimeClass != ".?AVC_CSPlayerPawn@@" || state.Warnings.Count != 0)
            throw new InvalidOperationException("Camera recovery requires an established inactive-player bug state.");
    }
}

internal sealed record CameraExperimentJournal(int ProcessId, long StartTicks, int Build,
    uint EntityHandle, ulong Pawn, ulong Address, float Original);

internal static class CameraExperiment
{
    private static string JournalPath => Path.Combine(AppContext.BaseDirectory, "captures", "camera-experiment.json");

    internal static void Run(GameReader reader, bool restoreOnly = false, bool maintain = false, CancellationToken sessionStop = default)
    {
        using var mutex = new Mutex(false, $"Local\\CameraProbe-CameraExperiment-{reader.ProcessId}");
        bool acquired;
        try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new InvalidOperationException("Another camera experiment is running.");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(sessionStop);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            reader.VerifyCameraExperimentCode();
            if (restoreOnly) { Restore(reader); return; }
            if (maintain) Restore(reader);
            if (File.Exists(JournalPath)) throw new InvalidOperationException("Run camera-restore before another experiment.");
            Console.WriteLine(maintain ? "Camera recovery is watching for the recorded bug state. Ctrl+C restores changes." :
                "Waiting up to 90 seconds for the captured camera bug state...");
            var recovery = maintain ? new CameraRecoveryState() : null;
            do
            {
            var waiting = Stopwatch.StartNew();
            (Snapshot State, ulong Pawn, ulong DeathTimeAddress) target;
            while (true)
            {
                stop.Token.ThrowIfCancellationRequested();
                if (reader.HasExited) return;
                try { target = reader.CameraExperimentTarget(recovery); break; }
                catch (Exception ex) when ((ex is InvalidOperationException or Win32Exception) && !reader.HasExited &&
                    (maintain || waiting.ElapsedMilliseconds < 90000))
                { stop.Token.WaitHandle.WaitOne(100); }
            }
            float original;
            try { original = reader.Read<float>(target.DeathTimeAddress); }
            catch (Exception ex) when (maintain && ex is InvalidOperationException or Win32Exception)
            {
                if (reader.HasExited) return;
                stop.Token.WaitHandle.WaitOne(100);
                continue;
            }
            if (!float.IsFinite(original) || CameraExperimentPolicy.OwnsValue(original))
                throw new InvalidOperationException("Unexpected existing death timestamp.");
            var journal = new CameraExperimentJournal(reader.ProcessId, reader.StartTicks, reader.Build,
                target.State.ControllerPawn!.Value, target.Pawn, target.DeathTimeAddress, original);
            Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
            using var writer = new StreamWriter(Path.Combine(Path.GetDirectoryName(JournalPath)!, $"camera-experiment-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl")) { AutoFlush = true };
            void Log(string kind, object value) => writer.WriteLine(JsonSerializer.Serialize(new { kind, value }, Program.Json));
            using var handle = OpenWrite(reader);
            bool applicationCompleted = false;
            bool applicationAttempted = false;
            try
            {
                JournalStorage.Publish(JournalPath, file => JsonSerializer.Serialize(file, journal));
                Log("before", target.State);
                var fresh = reader.CameraExperimentTarget(recovery);
                if (fresh.Pawn != target.Pawn || fresh.State.ControllerPawn != journal.EntityHandle ||
                    reader.Read<float>(journal.Address) != original)
                    throw new InvalidOperationException("Pawn changed before experiment.");
                stop.Token.ThrowIfCancellationRequested();
                applicationAttempted = true;
                Write(handle, journal.Address, CameraExperimentPolicy.TemporaryDeathTime);
                applicationCompleted = true;
                Console.WriteLine(maintain ? "Camera recovery active for the current pawn." :
                    "Camera experiment: move the mouse for 15 seconds. Original timestamp will be restored.");
                var clock = Stopwatch.StartNew();
                long nextLog = 0;
                while ((maintain || clock.ElapsedMilliseconds < 15000) && !stop.IsCancellationRequested && !reader.HasExited)
                {
                    Snapshot state;
                    try
                    {
                        if (!reader.MatchesCameraPawn(journal.EntityHandle, journal.Pawn)) break;
                        state = reader.Capture();
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                    {
                        Log("state-transition", new { error = ex.Message });
                        break;
                    }
                    if (!maintain || clock.ElapsedMilliseconds >= nextLog)
                    {
                        Log("during", state);
                        if (maintain && state.Health == 0)
                        {
                            try { Log("movement", reader.CaptureMovement()); }
                            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                            { Log("movement-unavailable", new { error = ex.Message }); }
                        }
                        nextLog = clock.ElapsedMilliseconds + 250;
                    }
                    try
                    {
                        if (recovery is not null) recovery.RequireContinuation(target.State, state);
                        else CameraExperimentPolicy.RequireBug(state);
                    }
                    catch (InvalidOperationException)
                    {
                        Log("recovery-ended", state);
                        break;
                    }
                    if (state.ControllerPawn != journal.EntityHandle) break;
                    if (!CameraExperimentPolicy.OwnsValue(state.DeathTime ?? float.NaN)) break;
                    stop.Token.WaitHandle.WaitOne(50);
                }
            }
            catch (Exception ex) when (maintain && !applicationAttempted && ex is InvalidOperationException or Win32Exception)
            { Log("application-skipped", new { error = ex.Message }); }
            finally
            {
                if (applicationAttempted) Restore(reader, applicationCompleted);
                else if (File.Exists(JournalPath)) File.Move(JournalPath, JournalPath + $".not-applied-{Guid.NewGuid():N}");
            }
            if (!reader.HasExited)
            {
                try { Log("after", reader.Capture()); }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                { Log("after-unavailable", new { error = ex.Message }); }
            }
            if (maintain) stop.Token.WaitHandle.WaitOne(100);
            } while (maintain && !stop.IsCancellationRequested && !reader.HasExited);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex) when (maintain && reader.HasExited && ex is InvalidOperationException or Win32Exception) { }
        finally { Console.CancelKeyPress -= cancel; mutex.ReleaseMutex(); }
    }

    private static void Restore(GameReader reader, bool applicationCompleted = false)
    {
        if (!File.Exists(JournalPath)) return;
        var journal = JsonSerializer.Deserialize<CameraExperimentJournal>(File.ReadAllText(JournalPath))
            ?? throw new InvalidOperationException("Invalid camera journal.");
        if (!reader.HasExited && journal.ProcessId == reader.ProcessId && journal.StartTicks == reader.StartTicks &&
            reader.MatchesCameraPawn(journal.EntityHandle, journal.Pawn))
        {
            _ = PlayerCodeLayout.ForBuild(journal.Build);
            if (journal.Build != reader.Build || journal.Address != journal.Pawn + 5208 || !float.IsFinite(journal.Original))
                throw new InvalidOperationException("Invalid camera restoration target.");
            float current = reader.Read<float>(journal.Address);
            if (CameraExperimentPolicy.ShouldRestore(current, journal.Original, applicationCompleted))
            {
                using var handle = OpenWrite(reader);
                Write(handle, journal.Address, journal.Original);
                if (reader.Read<float>(journal.Address) != journal.Original)
                    throw new InvalidOperationException("Camera restoration was not confirmed; journal retained.");
            }
        }
        File.Move(JournalPath, JournalPath + $".restored-{Guid.NewGuid():N}");
        Console.WriteLine("Camera experiment closed; no owned timestamp remains on the recorded pawn.");
    }

    private static SafeProcessHandle OpenWrite(GameReader reader)
    {
        if (reader.HasExited) throw new InvalidOperationException("Original CS2 process exited.");
        var handle = Native.OpenProcess(0x20 | 0x08, false, reader.ProcessId);
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        if (reader.HasExited) { handle.Dispose(); throw new InvalidOperationException("CS2 exited while opening the process."); }
        return handle;
    }

    private static void Write(SafeProcessHandle handle, ulong address, float value)
    {
        Validation.Pointer(address);
        if (!Native.WriteProcessMemory(handle, (nint)address, BitConverter.GetBytes(value), 4, out var count))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Camera timestamp write failed");
        Validation.ReadLength(count, 4);
    }
}
