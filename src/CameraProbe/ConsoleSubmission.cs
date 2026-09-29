namespace CameraProbe;

internal static class ConsoleSubmission
{
    internal static void Execute(string command, Action<string> type, Action<int> wait,
        Action guard, Action enter)
    {
        type(command);
        // The game's text-input queue must be processed before the Enter key-down arrives.
        wait(120);
        guard();
        enter();
    }
}
