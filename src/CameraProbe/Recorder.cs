using System.Diagnostics;
using System.Text.Json;

namespace CameraProbe;
internal static class Recorder
{
    internal static int Run(GameReader reader, bool bindingsReady, int delay, CancellationToken sessionStop = default, bool cameraRecovery = false)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(sessionStop);
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            Console.WriteLine("F7: записать состояние камеры; F8: отменить запись; Ctrl+C: выход.");
            Console.WriteLine(bindingsReady
                ? "F6: применить изображение через консоль и переключить команды во время freeze time. Консоль: ~ / Ё."
                : "F6 отключена. Используйте play --enable-switching.");
            Console.WriteLine(cameraRecovery ? "Экспериментальное восстановление камеры включено; результат проверяется в игре." : "Режим диагностики: камера не изменяется.");
            var previous = new Dictionary<int, bool> { [0x75] = Down(0x75), [0x76] = Down(0x76) };
            while (!stop.IsCancellationRequested && !reader.HasExited)
            {
                foreach (int key in previous.Keys.ToArray())
                {
                    bool down = Down(key), pressed = down && !previous[key];
                    previous[key] = down;
                    if (!pressed || !Native.IsForeground(reader.ProcessId)) continue;
                    if (key == 0x75 && !bindingsReady) continue;
                    try { Capture(reader, key == 0x75, delay, stop.Token, cameraRecovery); }
                    catch (Exception ex) { Console.Error.WriteLine($"Операция остановлена: {ex.Message}"); }
                    // Holding or pressing a key during collection must not queue another run.
                    previous[0x75] = Down(0x75);
                    previous[0x76] = Down(0x76);
                }
                stop.Token.WaitHandle.WaitOne(15);
            }
            return 0;
        }
        finally { Console.CancelKeyPress -= onCancel; }
    }

    private static bool Down(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
    private static void CheckCancellation(CancellationToken stop)
    {
        stop.ThrowIfCancellationRequested();
        if (Down(0x77)) throw new OperationCanceledException("F8: отменено");
    }
    private static void Capture(GameReader reader, bool switchTeams, int delay, CancellationToken stop, bool cameraRecovery)
    {
        CheckCancellation(stop);
        Snapshot initial = switchTeams ? reader.CaptureSwitchState() : reader.Capture();
        if (switchTeams)
            _ = SwitchPolicy.Teams(initial.Team ?? 0, initial.FreezeTime == true, Native.IsForeground(reader.ProcessId), delay);
        string directory = Path.Combine(AppContext.BaseDirectory, "captures");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"camera-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.jsonl");
        using var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        var clock = Stopwatch.StartNew();
        void Log(string kind, object value) => writer.WriteLine(JsonSerializer.Serialize(new { elapsedMs = clock.ElapsedMilliseconds, kind, value }, Program.Json));
        Log("metadata", new { tool = "CameraProbe", cameraRecoveryEnabled = cameraRecovery, switchTeams, delay, engineBuild = initial.EngineBuild });
        Log("before", initial);
        try { Log("convars", ConVarReader.Find(reader).Select(e => new { e.Name, e.Value, flags = $"0x{e.Flags:X}" })); }
        catch (Exception ex) { Log("convar-error", new { message = ex.Message }); }
        Console.WriteLine($"Запись: {path}");
        try
        {
            if (switchTeams)
            {
                try { Log("camera-before-input", reader.Capture()); }
                catch (Exception ex) { Log("camera-before-error", new { message = ex.Message }); }
                var channel = new ConsoleChannel(reader.ProcessId, () => CheckCancellation(stop));
                try
                {
                    // Do not let a held F6 repeat into the console.
                    while (Down(0x75)) channel.Wait(10);
                    channel.Open();
                    void ApplyImages()
                    {
                        try { channel.ApplyImages(reader); }
                        catch
                        {
                            Console.Error.WriteLine("Применение изображения прервано: spec_freeze_time мог остаться 1001/1002. Для завершения вручную: " + ImageCommands.Target);
                            try { Log("image-commands-partial", ConVarReader.Find(reader).Select(e => new { e.Name, e.Value })); }
                            catch (Exception ex) { Log("image-commands-state-unavailable", new { message = ex.Message }); }
                            throw;
                        }
                        Log("image-commands-verified", new { spec_freeze_time = 1000, mat_fullbright = 1 });
                        Console.WriteLine("F6: spec_freeze_time=1000, mat_fullbright=1 — значения проверены.");
                    }
                    void CheckBeforeSubmit()
                    {
                        var current = reader.CaptureSwitchState();
                        CheckCancellation(stop);
                        if (current.RoundStartCount != initial.RoundStartCount) throw new InvalidOperationException("Раунд сменился во время настройки консоли.");
                        _ = SwitchPolicy.Teams(current.Team ?? 0, current.FreezeTime == true, Native.IsForeground(reader.ProcessId), delay);
                    }
                    ImageCommands.RoundTrip(ApplyImages, () =>
                    {
                        CheckBeforeSubmit();
                        SwitchSequence.Execute(reader.CaptureSwitchState, () => Native.IsForeground(reader.ProcessId),
                            () => CheckCancellation(stop), team => channel.Join(team, CheckBeforeSubmit),
                            (milliseconds, guard) =>
                            {
                                long returnAt = clock.ElapsedMilliseconds + milliseconds;
                                do { guard(); channel.Wait(5); }
                                while (clock.ElapsedMilliseconds < returnAt);
                                guard();
                            }, Log, delay);
                    }, () =>
                    {
                        channel.Wait(150);
                        var after = reader.CaptureSwitchState();
                        if (after.RoundStartCount != initial.RoundStartCount || after.Team != initial.Team)
                            throw new InvalidOperationException("Возврат в исходную команду не подтверждён; повторное применение остановлено.");
                        Log("after-team-switch", after);
                    });
                    Log("image-commands-after-switch-verified", new { spec_freeze_time = 1000, mat_fullbright = 1 });
                }
                finally
                {
                    try { channel.Close(); }
                    catch (Exception ex) { Log("console-close-error", new { message = ex.Message }); }
                }
            }
            var samplingClock = Stopwatch.StartNew();
            while (samplingClock.Elapsed < TimeSpan.FromSeconds(8))
            {
                CheckCancellation(stop);
                try { Log("sample", reader.Capture()); }
                catch (Exception ex) { Log("sample-error", new { message = ex.Message }); }
                stop.WaitHandle.WaitOne(50);
            }
            Log("end", new { result = "capture-complete", cameraRecoveryEnabled = cameraRecovery });
            Console.WriteLine("Запись завершена. Это не подтверждение восстановления камеры.");
        }
        catch (Exception ex)
        {
            Log("aborted", new { message = ex.Message, note = "The first team input may already have been delivered." });
            throw;
        }
    }
}
