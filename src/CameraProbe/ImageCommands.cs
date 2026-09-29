namespace CameraProbe;

internal static class ImageCommands
{
    internal const string Target = "spec_freeze_time 1000; mat_fullbright 1";

    internal static void RoundTrip(Action apply, Action switchTeams, Action validateAfter)
    {
        apply();
        switchTeams();
        validateAfter();
        apply();
    }

    internal static void Apply(Action<string> send, Func<(int Fullbright, float Freeze)> read, Action<int> wait)
    {
        // A fresh value transition verifies that this console actually received our commands,
        // even when the requested final values were already set by an earlier F6 press.
        float probe = read().Freeze == 1001 ? 1002 : 1001;
        send($"spec_freeze_time {probe.ToString(System.Globalization.CultureInfo.InvariantCulture)}; mat_fullbright 1");
        Confirm(probe);
        send(Target);
        Confirm(1000);

        void Confirm(float freeze)
        {
            for (int attempt = 0; attempt < 15; attempt++)
            {
                wait(20);
                if (read() == (1, freeze)) return;
            }
            throw new InvalidOperationException("Консоль не подтвердила команды. Смена команд отменена. Проверьте консоль на клавише ~ / Ё и отсутствие открытых диалогов.");
        }
    }
}
