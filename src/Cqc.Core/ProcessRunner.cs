using System.Diagnostics;

namespace Cqc.Core;

public sealed record CommandResult(int ExitCode, string Output, string Error);
public sealed record ProcessCommand
{
    public required string Executable { get; init; }
    public required IEnumerable<string> Arguments { get; init; }
    public required string Directory { get; init; }
    public string? Input { get; init; }
}

public static class ProcessRunner
{
    public static Task<CommandResult> RunAsync(string executable, IEnumerable<string> arguments, string directory, CancellationToken cancellationToken)
        => RunAsync(new ProcessCommand { Executable = executable, Arguments = arguments, Directory = directory }, cancellationToken);

    public static async Task<CommandResult> RunAsync(ProcessCommand command, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(command.Executable) { WorkingDirectory = command.Directory,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = command.Input is not null, UseShellExecute = false };
        foreach (var argument in command.Arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException($"Unable to start {command.Executable}.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            if (command.Input is not null)
            {
                await process.StandardInput.WriteAsync(command.Input.AsMemory(), cancellationToken);
                process.StandardInput.Close();
            }
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        return new(process.ExitCode, await output, await error);
    }
}
