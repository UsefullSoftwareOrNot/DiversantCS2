using System.Diagnostics;

namespace CameraProbe;

internal sealed record DumperResult(int ExitCode, bool TimedOut, string StandardOutput, string StandardError);
internal delegate DumperResult DumperInvoker(string toolPath, string outputDirectory, TimeSpan timeout);

internal static class DumperRunner
{
    internal static DumperResult Run(string toolPath, string outputDirectory, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(2))
            throw new InvalidOperationException("Invalid cs2-dumper timeout.");
        var start = new ProcessStartInfo
        {
            FileName = toolPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("--file-types"); start.ArgumentList.Add("json");
        start.ArgumentList.Add("--output"); start.ArgumentList.Add(outputDirectory);
        start.ArgumentList.Add("--no-log-file");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start bundled cs2-dumper.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(checked((int)timeout.TotalMilliseconds)))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            process.WaitForExit();
            Task.WaitAll(stdout, stderr);
            return new DumperResult(-1, true, stdout.Result, stderr.Result);
        }
        Task.WaitAll(stdout, stderr);
        return new DumperResult(process.ExitCode, false, stdout.Result, stderr.Result);
    }
}
