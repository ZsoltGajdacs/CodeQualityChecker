using System.Diagnostics;

namespace Cqc.Core;

public sealed record CommandResult(int ExitCode, string Output, string Error);
public sealed record ProcessCommand
{
    public required string Executable { get; init; }
    public required IEnumerable<string> Arguments { get; init; }
    public required string Directory { get; init; }
    public string? Input { get; init; }
    public string[] RemoveEnvironmentVariables { get; init; } = [];
    internal IReadOnlyDictionary<string, string>? EnvironmentOverrides { get; init; }
}

public static class ProcessRunner
{
    public static Task<CommandResult> RunAsync(string executable, IEnumerable<string> arguments, string directory, CancellationToken cancellationToken)
        => RunAsync(new ProcessCommand { Executable = executable, Arguments = arguments, Directory = directory }, cancellationToken);

    public static async Task<CommandResult> RunAsync(ProcessCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(command.Executable) { WorkingDirectory = command.Directory,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = command.Input is not null, UseShellExecute = false };
        foreach (var argument in command.Arguments) start.ArgumentList.Add(argument);
        foreach (var name in command.RemoveEnvironmentVariables) start.Environment.Remove(name);
        if (command.EnvironmentOverrides is { } overrides)
            foreach (var entry in overrides) start.Environment[entry.Key] = entry.Value;
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
            return new(process.ExitCode, await output, await error);
        }
        catch
        {
            await StopAsync(process);
            throw;
        }
    }

    private static async Task StopAsync(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited) { }
        catch (System.ComponentModel.Win32Exception) when (process.HasExited) { }
        await process.WaitForExitAsync(CancellationToken.None);
    }
}
