using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CameraProbe;
internal static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(SafeProcessHandle process, nint address,
        byte[] buffer, nuint size, out nuint bytesRead);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WriteProcessMemory(SafeProcessHandle process, nint address,
        byte[] buffer, nuint size, out nuint bytesWritten);
    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint count, Input[] inputs, int size);

    // INPUT is 40 bytes on Windows x64. Union starts at offset 8; KEYBDINPUT is 24 bytes.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    internal struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort VirtualKey;
        [FieldOffset(10)] public ushort Scan;
        [FieldOffset(12)] public uint Flags;
        [FieldOffset(16)] public uint Time;
        [FieldOffset(24)] public nuint ExtraInfo;
    }

    internal static bool IsForeground(int pid)
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint foreground);
        return foreground == pid;
    }

    internal static void CheckInput(int pid)
    {
        if (!IsForeground(pid)) throw new InvalidOperationException("Focus changed; input stopped.");
        // Do not combine the team's key with modifiers held by the person using the computer.
        if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0))
            throw new InvalidOperationException("Release Shift/Ctrl/Alt/Windows before switching.");
    }

    internal static void Tap(ushort key, int pid, Action cancellationCheck) =>
        Stroke(new Input { Type = 1, VirtualKey = key }, pid, cancellationCheck);

    internal static void TapScan(ushort scan, int pid, Action cancellationCheck, int holdMilliseconds = 40) =>
        Stroke(new Input { Type = 1, Scan = scan, Flags = 8 }, pid, cancellationCheck, holdMilliseconds);

    internal static void TypeText(string text, int pid, Action cancellationCheck)
    {
        foreach (char character in text)
        {
            cancellationCheck();
            CheckInput(pid);
            var down = new Input { Type = 1, Scan = character, Flags = 4 };
            var up = down; up.Flags |= 2;
            uint sent = SendInput(2, [down, up], Marshal.SizeOf<Input>());
            if (sent != 2)
            {
                if (sent == 1) ReleaseInputs([up]);
                throw new InvalidOperationException("Console text input was interrupted.");
            }
        }
    }

    internal static void ClearConsoleLine(int pid, Action cancellationCheck)
    {
        cancellationCheck();
        CheckInput(pid);
        var release = new[] { new Input { Type = 1, VirtualKey = 0x41, Flags = 2 }, new Input { Type = 1, VirtualKey = 0x11, Flags = 2 } };
        try
        {
            Input[] keys = [new() { Type = 1, VirtualKey = 0x11 }, new() { Type = 1, VirtualKey = 0x41 }, ..release];
            if (SendInput(4, keys, Marshal.SizeOf<Input>()) != 4) throw new InvalidOperationException("Could not clear console input.");
        }
        finally { ReleaseInputs(release); }
        Tap(0x08, pid, cancellationCheck);
    }

    private static void Stroke(Input down, int pid, Action cancellationCheck, int holdMilliseconds = 12)
    {
        cancellationCheck();
        CheckInput(pid);
        var up = down; up.Flags |= 2;
        if (SendInput(1, [down], Marshal.SizeOf<Input>()) != 1) throw new InvalidOperationException("Key down was rejected.");
        try { Thread.Sleep(holdMilliseconds); }
        finally { ReleaseInputs([up]); }
        cancellationCheck();
    }

    internal static void ReleaseInputs(Input[] release, Func<Input[], uint>? sender = null)
    {
        sender ??= keys => SendInput((uint)keys.Length, keys, Marshal.SizeOf<Input>());
        if (sender(release) == (uint)release.Length) return;
        Thread.Sleep(5);
        if (sender(release) != (uint)release.Length)
            throw new InvalidOperationException("Key release was rejected; input stopped. Release the key manually.");
    }
}
