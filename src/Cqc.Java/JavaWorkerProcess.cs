using System.Diagnostics;
using Cqc.Core;

namespace Cqc.Java;

internal static class JavaWorkerProcess
{
    internal static async Task<CommandResult> RunAsync(ProcessStartInfo start, string input, CancellationToken token)
    {
        using var process = new Process { StartInfo = start };
        process.Start();
        using var registration = token.Register(() => Stop(process));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(token);
            var errors = process.StandardError.ReadToEndAsync(token);
            await process.StandardInput.WriteAsync(input.AsMemory(), token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(token);
            return new(process.ExitCode, await output, await errors);
        }
        finally { Stop(process); }
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }
}
