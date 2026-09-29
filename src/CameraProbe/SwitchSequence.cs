namespace CameraProbe;

internal static class SwitchSequence
{
    internal static void Execute(Func<Snapshot> capture, Func<bool> foreground,
        Action cancel, Action<ushort> tap, Action<int, Action> wait,
        Action<string, object> log, int delay)
    {
        // Caller prepares its files before entering this sequence. No file I/O between capture and Tap.
        Snapshot first = capture();
        ushort[] keys;
        try
        {
            cancel();
            keys = SwitchPolicy.Teams(first.Team ?? 0, first.FreezeTime == true, foreground(), delay);
            tap(keys[0]);
        }
        finally { log("before-input", first); }
        log("command-submitted", new { command = $"jointeam {keys[0]}", requestedTeam = keys[0] });
        wait(delay, () =>
        {
            cancel();
            if (!foreground()) throw new InvalidOperationException("Focus lost during delay; sequence aborted.");
        });
        Snapshot between = capture();
        try
        {
            cancel(); // Capture consists of several reads: cancellation may have happened during them.
            if (between.RoundStartCount != first.RoundStartCount)
                throw new InvalidOperationException("Round changed; return input cancelled.");
            _ = SwitchPolicy.Teams(between.Team ?? 0, between.FreezeTime == true, foreground(), delay);
            tap(keys[1]);
        }
        finally
        {
            // Preserve evidence on every guard/input failure, without inserting disk I/O before Tap.
            log("between", between);
        }
        log("command-submitted", new { command = $"jointeam {keys[1]}", requestedTeam = keys[1] });
    }
}
