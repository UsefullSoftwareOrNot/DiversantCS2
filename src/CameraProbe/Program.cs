using System.Text.Json;

namespace CameraProbe;
internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help")
            {
                Console.WriteLine("CameraProbe — переключение команд, диагностика и экспериментальное восстановление камеры CS2.\n" +
                    "  snapshot                    Считать состояние один раз\n" +
                    "  jump-record                 Запись движения после первого пробела (15 секунд)\n" +
                    "  movement-experiment         Пробел: локальный HP=10000 на 15 секунд с восстановлением\n" +
                    "  movement-restore            Восстановить прерванный эксперимент движения\n" +
                    "  camera-experiment           Проверка ветки камеры на 15 секунд с восстановлением\n" +
                    "  camera-restore              Восстановить прерванную проверку камеры\n" +
                    "  camera-recover              Удерживать исправление камеры в состоянии бага\n" +
                    "  watch                       F7: запись 8 секунд, F8: отмена\n" +
                    "  cvars                       Прочитать две ConVar и их ограничения\n" +
                    "  cvars-unlock                Временно снять локальные ограничения двух ConVar\n" +
                    "  cvars-restore               Восстановить флаги после прерванной сессии\n" +
                    "  play --enable-switching     F6: применить команды изображения и сменить команды\n" +
                    "  play --enable-switching --delay-ms 75\n" +
                    "Для F6 нужна включённая консоль на стандартной клавише ~ / Ё. Конфиг camera_probe не требуется.\n" +
                    "Файлы состояния сохраняются в captures рядом с приложением. Ctrl+C: выход.");
                return 0;
            }
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
                throw new InvalidOperationException("Requires Windows x64.");
            if (args[0] is not ("snapshot" or "jump-record" or "movement-experiment" or "movement-restore" or "watch" or "play" or "cvars" or "cvars-unlock" or "cvars-restore" or "camera-experiment" or "camera-restore" or "camera-recover")) throw new InvalidOperationException("Unknown command. Use --help.");
            bool bindingsReady = false;
            int delay = 75;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[0] is not ("watch" or "play")) throw new InvalidOperationException("This command takes no options.");
                if (args[i] is "--bindings-ready" or "--enable-switching") bindingsReady = true;
                else if (args[i] == "--delay-ms" && i + 1 < args.Length && int.TryParse(args[++i], out int value)) delay = value;
                else throw new InvalidOperationException("Invalid option. Use --help.");
            }
            _ = SwitchPolicy.Teams(2, true, true, delay);
            if (bindingsReady && args[0] != "play") throw new InvalidOperationException("Для F6 используйте play --enable-switching: применение изображения требует сессии ConVars.");
            bool conVarsOnly = args[0].StartsWith("cvars", StringComparison.Ordinal);
            using var reader = new GameReader(Path.Combine(AppContext.BaseDirectory, "reference"), conVarsOnly);
            if (args[0] is "movement-experiment" or "movement-restore")
            {
                MovementExperiment.Run(reader, args[0] == "movement-restore");
                return 0;
            }
            if (args[0] == "jump-record")
            {
                var waiting = System.Diagnostics.Stopwatch.StartNew();
                Console.WriteLine("Waiting for Space in CS2 (90 seconds); then recording movement for 15 seconds. Read-only.");
                while (!Native.IsForeground(reader.ProcessId) || (Native.GetAsyncKeyState(0x20) & 0x8000) == 0)
                {
                    if (reader.HasExited || waiting.ElapsedMilliseconds > 90000) throw new InvalidOperationException("Movement recording did not start.");
                    Thread.Sleep(20);
                }
                string directory = Path.Combine(AppContext.BaseDirectory, "captures");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"movement-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
                Console.WriteLine(path);
                using var writer = new StreamWriter(path) { AutoFlush = true };
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 15000 && !reader.HasExited)
                {
                    try { writer.WriteLine(JsonSerializer.Serialize(reader.CaptureMovement(), Json)); }
                    catch (Exception ex) { writer.WriteLine(JsonSerializer.Serialize(new { error = ex.Message }, Json)); }
                    Thread.Sleep(40);
                }
                return 0;
            }
            if (args[0] is "camera-experiment" or "camera-restore" or "camera-recover")
            {
                CameraExperiment.Run(reader, args[0] == "camera-restore", args[0] == "camera-recover");
                return 0;
            }
            if (args[0] == "play")
            {
                var entries = ConVarReader.Find(reader);
                ConVarSession.Restore(reader, entries);
                entries = ConVarReader.Find(reader);
                ConVarSession.Run(reader, entries, token =>
                {
                    using var interactionStop = CancellationTokenSource.CreateLinkedTokenSource(token);
                    var camera = Task.Run(() =>
                    {
                        try
                        {
                            using var cameraReader = new GameReader(Path.Combine(AppContext.BaseDirectory, "reference"));
                            if (cameraReader.ProcessId != reader.ProcessId || cameraReader.StartTicks != reader.StartTicks)
                                throw new InvalidOperationException("CS2 restarted before camera recovery began.");
                            CameraExperiment.Run(cameraReader, maintain: true, sessionStop: interactionStop.Token);
                        }
                        catch { interactionStop.Cancel(); throw; }
                    });
                    var movement = Task.Run(() =>
                    {
                        try
                        {
                            using var movementReader = new GameReader(Path.Combine(AppContext.BaseDirectory, "reference"));
                            if (movementReader.ProcessId != reader.ProcessId || movementReader.StartTicks != reader.StartTicks)
                                throw new InvalidOperationException("CS2 restarted before movement recovery began.");
                            MovementExperiment.Run(movementReader, automatic: true, sessionStop: interactionStop.Token);
                        }
                        catch { interactionStop.Cancel(); throw; }
                    });
                    try { Recorder.Run(reader, bindingsReady, delay, interactionStop.Token, cameraRecovery: true); }
                    finally { interactionStop.Cancel(); Task.WhenAll(camera, movement).GetAwaiter().GetResult(); }
                });
                return 0;
            }
            if (conVarsOnly)
            {
                var entries = ConVarReader.Find(reader);
                Console.Error.WriteLine($"ConVar registry validated for CS2 build {reader.Build}.");
                if (args[0] == "cvars-unlock") ConVarSession.Run(reader, entries);
                else if (args[0] == "cvars-restore") ConVarSession.Restore(reader, entries);
                else Console.WriteLine(JsonSerializer.Serialize(entries.Select(e => new
                {
                    e.Name, e.Type, e.Value, flags = $"0x{e.Flags:X}",
                    restriction = (e.Flags & ConVarPolicy.Mask(e.Name, e.Type)) != 0
                }), new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            if (args[0] == "snapshot")
            {
                Console.WriteLine(JsonSerializer.Serialize(reader.Capture(), new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            return Recorder.Run(reader, bindingsReady, delay);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ошибка: {ex.Message}");
            if (Environment.GetEnvironmentVariable("CAMERA_PROBE_DEBUG") == "1") Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}
