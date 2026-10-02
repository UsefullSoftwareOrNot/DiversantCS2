using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CameraProbe;

internal static class MovementExperimentPolicy
{
    internal const int TemporaryHealth = 10000;
    internal static void Require(Snapshot state)
    {
        CameraExperimentPolicy.RequireBug(state, continuing: true);
        if (state.Health != 0 || state.RoundStartCount is null || state.FreezeTime != false ||
            !CameraExperimentPolicy.OwnsValue(state.DeathTime ?? float.NaN))
            throw new InvalidOperationException("Requires HP=0, active camera recovery and an unfrozen round.");
    }

    internal static bool SameContext(Snapshot state, uint pawn, byte team, int round) =>
        state.ControllerPawn == pawn && state.PlayerPawn == pawn && state.Team == team &&
        state.RoundStartCount == round && state.LifeState == 2 && state.PawnIsAlive == false &&
        state.PawnRuntimeClass == ".?AVC_CSPlayerPawn@@" && state.Warnings.Count == 0;

    internal static bool ShouldRestore(int health, bool completed, bool sameContext, int temporaryHealth = TemporaryHealth)
    {
        if (health == 0) return false;
        if (health == temporaryHealth && sameContext) return true;
        if (health == temporaryHealth || !completed)
            throw new InvalidOperationException("Ambiguous health restoration; journal retained. Restart CS2 before retrying.");
        return false;
    }
}

internal sealed record MovementJournal(int ProcessId, long StartTicks, int Build,
    uint EntityHandle, ulong Pawn, ulong Address, byte Team, int Round, int TemporaryHealth = 1);

internal static class MovementExperiment
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "captures");
    private static string JournalPath => Path.Combine(DirectoryPath, "movement-experiment.json");

    internal static void Run(GameReader reader, bool restoreOnly = false, bool automatic = false,
        CancellationToken sessionStop = default)
    {
        using var mutex = new Mutex(false, $"Local\\CameraProbe-MovementExperiment-{reader.ProcessId}");
        bool acquired;
        try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new InvalidOperationException("A movement experiment is already running.");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(sessionStop);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            reader.VerifyMovementExperimentCode();
            Restore(reader, completed: false);
            if (restoreOnly) return;
            Console.WriteLine(automatic ? "Automatic local HP=10000: waiting for the captured HP=0 bug state." :
                "Movement experiment: at HP=0 with camera recovery active, press Space. Local HP=10000 for up to 15 seconds.");
            do
            {
            string? lastRejection = null;
            (Snapshot State, ulong Pawn, ulong HealthAddress) target;
            while (true)
            {
                stop.Token.ThrowIfCancellationRequested();
                if (reader.HasExited) return;
                if (automatic || (Native.IsForeground(reader.ProcessId) && (Native.GetAsyncKeyState(0x20) & 0x8000) != 0))
                {
                    try { target = reader.MovementExperimentTarget(); break; }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                    {
                        if (!automatic && lastRejection != ex.Message)
                        {
                            Console.WriteLine($"Still waiting: {ex.Message}");
                            lastRejection = ex.Message;
                        }
                    }
                }
                stop.Token.WaitHandle.WaitOne(25);
            }
            Directory.CreateDirectory(DirectoryPath);
            string path = Path.Combine(DirectoryPath, $"movement-experiment-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
            using var log = new StreamWriter(path) { AutoFlush = true };
            void Log(string kind, object value) => log.WriteLine(JsonSerializer.Serialize(new { kind, value }, Program.Json));
            var journal = new MovementJournal(reader.ProcessId, reader.StartTicks, reader.Build,
                target.State.ControllerPawn!.Value, target.Pawn, target.HealthAddress,
                target.State.Team!.Value, target.State.RoundStartCount!.Value, MovementExperimentPolicy.TemporaryHealth);
            bool attempted = false, completed = false;
            try
            {
                Log("before", reader.CaptureMovement());
                JournalStorage.Publish(JournalPath, file => JsonSerializer.Serialize(file, journal));
                var fresh = reader.MovementExperimentTarget();
                if (fresh.Pawn != journal.Pawn || !MovementExperimentPolicy.SameContext(fresh.State,
                    journal.EntityHandle, journal.Team, journal.Round))
                    throw new InvalidOperationException("Player changed before movement experiment.");
                stop.Token.ThrowIfCancellationRequested();
                Write(reader, journal, journal.TemporaryHealth, stop.Token, () => attempted = true);
                completed = true;
                if (reader.Read<int>(journal.Address) != journal.TemporaryHealth)
                    throw new InvalidOperationException("Temporary health did not persist; stopping.");
                Console.WriteLine(automatic ? $"Local HP=10000 active. Log: {path}" :
                    $"Experiment active for 15 seconds. Try W/A/S/D and Space. Log: {path}");
                var clock = Stopwatch.StartNew();
                while ((automatic || clock.ElapsedMilliseconds < 15000) && !stop.IsCancellationRequested && !reader.HasExited)
                {
                    var state = reader.Capture();
                    if (!MovementExperimentPolicy.SameContext(state, journal.EntityHandle, journal.Team, journal.Round) ||
                        state.Health != journal.TemporaryHealth || state.FreezeTime != false || !CameraExperimentPolicy.OwnsValue(state.DeathTime ?? float.NaN))
                    {
                        Log("state-ended", state);
                        break;
                    }
                    Log("during", reader.CaptureMovement());
                    stop.Token.WaitHandle.WaitOne(automatic ? 250 : 50);
                }
            }
            catch (Exception ex) when (automatic && (!attempted || completed) && ex is InvalidOperationException or Win32Exception)
            { Log(completed ? "state-transition" : "application-skipped", new { error = ex.Message }); }
            finally
            {
                if (attempted) Restore(reader, completed);
                else if (File.Exists(JournalPath)) File.Move(JournalPath, JournalPath + $".not-applied-{Guid.NewGuid():N}");
            }
            if (!reader.HasExited)
            {
                try { Log("after", reader.CaptureMovement()); }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                { Log("after-unavailable", new { error = ex.Message }); }
            }
            if (!automatic) Console.WriteLine("Experiment finished.");
            if (automatic) stop.Token.WaitHandle.WaitOne(250);
            } while (automatic && !stop.IsCancellationRequested && !reader.HasExited);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { Console.CancelKeyPress -= cancel; mutex.ReleaseMutex(); }
    }

    private static void Restore(GameReader reader, bool completed)
    {
        if (!File.Exists(JournalPath)) return;
        var j = JsonSerializer.Deserialize<MovementJournal>(File.ReadAllText(JournalPath))
            ?? throw new InvalidOperationException("Invalid movement journal.");
        _ = PlayerCodeLayout.ForBuild(j.Build);
        if (j.Address != j.Pawn + 844 || j.Team is not (2 or 3) || j.TemporaryHealth is not (1 or 10000))
            throw new InvalidOperationException("Invalid movement restoration target.");
        if (!reader.HasExited && j.ProcessId == reader.ProcessId && j.StartTicks == reader.StartTicks &&
            reader.MatchesCameraPawn(j.EntityHandle, j.Pawn))
        {
            if (j.Build != reader.Build) throw new InvalidOperationException("Movement journal build does not match CS2.");
            var state = reader.Capture();
            int health = reader.Read<int>(j.Address);
            if (MovementExperimentPolicy.ShouldRestore(health, completed,
                MovementExperimentPolicy.SameContext(state, j.EntityHandle, j.Team, j.Round), j.TemporaryHealth))
            {
                // Recheck both value and full pawn identity before restoring the recorded zero.
                if (reader.Read<int>(j.Address) != j.TemporaryHealth) throw new InvalidOperationException("Health changed during restoration; journal retained.");
                Write(reader, j, 0);
                if (reader.Read<int>(j.Address) != 0) throw new InvalidOperationException("Health restoration was not confirmed; journal retained.");
            }
        }
        File.Move(JournalPath, JournalPath + $".closed-{Guid.NewGuid():N}");
        Console.WriteLine("Movement journal closed; any newer game health value was preserved.");
    }

    private static void Write(GameReader reader, MovementJournal journal, int health, CancellationToken cancellation = default,
        Action? beforeNativeWrite = null)
    {
        if (reader.HasExited || !reader.MatchesCameraPawn(journal.EntityHandle, journal.Pawn))
            throw new InvalidOperationException("Movement target no longer exists.");
        using var handle = Native.OpenProcess(0x20 | 0x08, false, reader.ProcessId);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (reader.HasExited) throw new InvalidOperationException("CS2 exited before movement write.");
        var current = reader.Capture();
        if (!MovementExperimentPolicy.SameContext(current, journal.EntityHandle, journal.Team, journal.Round))
            throw new InvalidOperationException("Player context changed before movement write.");
        if (health == journal.TemporaryHealth) MovementExperimentPolicy.Require(current);
        if (reader.Read<int>(journal.Address) != (health == journal.TemporaryHealth ? 0 : journal.TemporaryHealth))
            throw new InvalidOperationException("Health changed before movement write.");
        Validation.Pointer(journal.Address);
        cancellation.ThrowIfCancellationRequested();
        beforeNativeWrite?.Invoke();
        if (!Native.WriteProcessMemory(handle, (nint)journal.Address, BitConverter.GetBytes(health), 4, out var count))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Movement experiment write failed");
        Validation.ReadLength(count, 4);
    }
}
