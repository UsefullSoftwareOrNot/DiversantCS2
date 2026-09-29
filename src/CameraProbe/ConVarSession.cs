using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace CameraProbe;

internal sealed record ConVarJournal(int ProcessId, long StartTicks, int Build, ConVarEntry[] Entries);

internal static class ConVarSession
{
    internal static string JournalPath => Path.Combine(AppContext.BaseDirectory, "captures", "cvars-session.json");

    internal static void Run(GameReader reader, ConVarEntry[] entries, Action<CancellationToken>? interaction = null)
    {
        using var mutex = new Mutex(false, $"Local\\CameraProbe-ConVars-{reader.ProcessId}");
        bool acquired;
        try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new InvalidOperationException("Another ConVar session is active.");
        try { RunLocked(reader, entries, interaction); }
        finally { mutex.ReleaseMutex(); }
    }

    private static void RunLocked(GameReader reader, ConVarEntry[] entries, Action<CancellationToken>? interaction)
    {
        if (File.Exists(JournalPath))
            throw new InvalidOperationException("An earlier session journal exists. Run cvars-restore first.");
        var journal = new ConVarJournal(reader.ProcessId, reader.StartTicks, reader.Build, entries);
        foreach (var entry in entries)
            if (ConVarReader.VerifyAndReadFlags(reader, entry) != entry.Flags)
                throw new InvalidOperationException("ConVar flags changed since inspection.");
        using var writeHandle = OpenWrite(reader);
        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
        JournalStorage.Publish(JournalPath, file => JsonSerializer.Serialize(file, journal));
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            foreach (var entry in entries)
            {
                stop.Token.ThrowIfCancellationRequested();
                ulong current = ConVarReader.VerifyAndReadFlags(reader, entry);
                if (current != entry.Flags) throw new InvalidOperationException("ConVar flags changed before application.");
                ulong replacement = ConVarPolicy.Unlock(entry.Name, entry.Type, current);
                WriteFlags(writeHandle, entry.Address + 0x30, replacement);
                if (ConVarReader.VerifyAndReadFlags(reader, entry) != replacement)
                    throw new InvalidOperationException($"Flag change for {entry.Name} did not persist.");
                Console.WriteLine($"{entry.Name}: flags 0x{current:X} -> 0x{replacement:X}");
            }
            Console.WriteLine(interaction == null
                ? "Локальные флаги изменены. В консоли CS2 выполните:\nspec_freeze_time 1000\nmat_fullbright 1"
                : "Локальные флаги изменены. Команды изображения будут применяться при каждом F6.");
            Console.WriteLine("Для возврата значений перед выходом выполните:");
            foreach (var entry in entries) Console.WriteLine($"{entry.Name} {entry.Value}");
            Console.WriteLine("Ctrl+C восстановит только флаги. Изменённые в консоли значения автоматически не сбрасываются.");
            Console.WriteLine("Запись флагов проверена; выполнение команд и эффект изображения этим не подтверждены.");
            if (interaction != null) interaction(stop.Token);
            else while (!stop.IsCancellationRequested && !reader.HasExited) stop.Token.WaitHandle.WaitOne(100);
        }
        finally
        {
            try { RestoreJournal(reader, writeHandle, journal); }
            finally { Console.CancelKeyPress -= cancel; }
        }
    }

    internal static void Restore(GameReader reader, ConVarEntry[] live)
    {
        using var mutex = new Mutex(false, $"Local\\CameraProbe-ConVars-{reader.ProcessId}");
        bool acquired;
        try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new InvalidOperationException("Stop the active ConVar session with Ctrl+C first.");
        ConsoleCancelEventHandler protectRestore = (_, e) => { e.Cancel = true; };
        Console.CancelKeyPress += protectRestore;
        try
        {
            if (!File.Exists(JournalPath)) { Console.WriteLine("Нет сохранённой сессии ConVar."); return; }
            var journal = JsonSerializer.Deserialize<ConVarJournal>(File.ReadAllText(JournalPath))
                ?? throw new InvalidOperationException("Invalid session journal.");
            if (journal.ProcessId != reader.ProcessId || journal.StartTicks != reader.StartTicks)
            {
                File.Move(JournalPath, JournalPath + $".old-{Guid.NewGuid():N}");
                Console.WriteLine("Игра перезапущена; старый журнал архивирован. Запись в память не выполнялась.");
                return;
            }
            if (journal.Build != reader.Build || journal.Entries.Length != 2 ||
                journal.Entries.Select(e => e.Name).Distinct().Count() != 2)
                throw new InvalidOperationException("Invalid journal build or entries.");
            foreach (var entry in journal.Entries)
            {
                var actual = live.SingleOrDefault(e => e.Name == entry.Name);
                if (actual == null) throw new InvalidOperationException("ConVar disappeared from the active registry.");
                ConVarRecovery.ValidateIdentity(entry, actual);
            }
            using var handle = OpenWrite(reader);
            RestoreJournal(reader, handle, journal);
        }
        finally { Console.CancelKeyPress -= protectRestore; mutex.ReleaseMutex(); }
    }

    private static void RestoreJournal(GameReader reader, SafeProcessHandle handle, ConVarJournal journal)
    {
        string[] errors = [];
        if (!reader.HasExited)
        {
            errors = ConVarRecovery.RestoreFlags(journal.Entries, entry =>
            {
                var current = ConVarReader.Find(reader)
                    .Single(e => e.Name == entry.Name);
                ConVarRecovery.ValidateIdentity(entry, current);
                return ConVarReader.VerifyAndReadFlags(reader, current);
            }, (entry, value) => WriteFlags(handle, entry.Address + 0x30, value));
        }
        if (errors.Length > 0)
            throw new InvalidOperationException("Не удалось полностью восстановить флаги; журнал сохранён. " + string.Join("; ", errors));
        File.Move(JournalPath, JournalPath + $".restored-{Guid.NewGuid():N}");
        Console.WriteLine(reader.HasExited ? "CS2 завершена; журнал закрыт." : "Исходные биты ограничений восстановлены.");
    }

    private static SafeProcessHandle OpenWrite(GameReader reader)
    {
        if (reader.HasExited) throw new InvalidOperationException("CS2 exited before opening its write handle.");
        var handle = Native.OpenProcess(0x20 | 0x08, false, reader.ProcessId);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "Cannot open CS2 for the requested flag changes");
        }
        if (reader.HasExited)
        {
            handle.Dispose();
            throw new InvalidOperationException("Original CS2 process exited; refusing a possibly reused process ID.");
        }
        return handle;
    }

    internal static void WriteFlags(SafeProcessHandle handle, ulong address, ulong flags)
    {
        Validation.Pointer(address);
        byte[] bytes = BitConverter.GetBytes(flags);
        if (!Native.WriteProcessMemory(handle, (nint)address, bytes, 8, out nuint count))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Flag write failed");
        Validation.ReadLength(count, 8);
    }
}
