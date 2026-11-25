namespace Cqc.Core;

/// <summary>Runs Git queries using only objects available in the checkout.</summary>
internal static class LocalGit
{
    private static readonly IReadOnlyDictionary<string, string> LocalEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["GIT_NO_LAZY_FETCH"] = "1",
        ["GIT_ALLOW_PROTOCOL"] = ""
    };

    internal static async Task<string?> ReadHeadAsync(ProjectSnapshot snapshot, CancellationToken token)
    {
        var result = await RunAsync(snapshot.Root, token, ["rev-parse", "--verify", "--quiet", "HEAD^{commit}"]);
        return result.ExitCode == 1 ? null : GitScope.OutputOrThrow(result).Trim();
    }

    internal static async Task<string> OutputAsync(string root, CancellationToken token, params string[] args) =>
        GitScope.OutputOrThrow(await RunAsync(root, token, args));

    private static Task<CommandResult> RunAsync(string root, CancellationToken token, string[] args) =>
        ProcessRunner.RunAsync(new ProcessCommand
        {
            Executable = "git", Directory = root, Arguments = args, EnvironmentOverrides = LocalEnvironment
        }, token);
}
