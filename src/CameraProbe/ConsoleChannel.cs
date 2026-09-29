using System.Diagnostics;

namespace CameraProbe;

internal sealed class ConsoleChannel(int processId, Action cancel)
{
    private bool verified;

    internal void Guard() { cancel(); Native.CheckInput(processId); }

    internal void Wait(int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        do { Guard(); Thread.Sleep(5); } while (timer.ElapsedMilliseconds < milliseconds);
        Guard();
    }

    internal void Open()
    {
        // Escape dismisses chat, existing console text or modal UI before the console key.
        Native.Tap(0x1B, processId, cancel);
        Wait(80);
        Native.TapScan(0x29, processId, cancel); // physical ~ / Ё key
        Wait(160);
    }

    internal void ApplyImages(GameReader reader)
    {
        ImageCommands.Apply(command => Send(command), () =>
        {
            var entries = ConVarReader.Find(reader);
            return (int.Parse(entries.Single(e => e.Name == "mat_fullbright").Value, System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(entries.Single(e => e.Name == "spec_freeze_time").Value, System.Globalization.CultureInfo.InvariantCulture));
        }, Wait);
        verified = true;
    }

    internal void Send(string command, Action? beforeSubmit = null)
    {
        Guard();
        Native.ClearConsoleLine(processId, cancel);
        ConsoleSubmission.Execute(command, text => Native.TypeText(text, processId, cancel), Wait,
            () => { Guard(); beforeSubmit?.Invoke(); },
            () => Native.TapScan(0x1C, processId, cancel, holdMilliseconds: 80));
        // Do not clear the line for the next command before this Enter has been processed.
        Wait(50);
    }

    internal void Join(ushort team, Action beforeSubmit)
    {
        if (!verified || team is not (2 or 3)) throw new InvalidOperationException("Console is not verified for team commands.");
        Send($"jointeam {team}", beforeSubmit);
    }

    internal void Close()
    {
        if (verified) Send("hideconsole; gameui_hide");
    }
}
