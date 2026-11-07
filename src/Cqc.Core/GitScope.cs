namespace Cqc.Core;

internal static class GitScope
{
    public static async Task<string[]> RemoveIgnoredAsync(string root, string[] files, CancellationToken token)
    {
        try
        {
            var probe = await ProcessRunner.RunAsync("git", ["-C", root, "rev-parse", "--show-toplevel"], root, token);
            if (probe.ExitCode != 0) return files;
            var ignored = new HashSet<string>(StringComparer.Ordinal);
            foreach (var batch in files.Chunk(100))
            {
                var command = new ProcessCommand { Executable = "git", Arguments = ["check-ignore", "--no-index", "-z", "--stdin"], Directory = root, Input = string.Join('\0', batch) + '\0' };
                var result = await ProcessRunner.RunAsync(command, token);
                if (result.ExitCode > 1) throw new IOException("git check-ignore failed: " + result.Error);
                foreach (var file in result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)) ignored.Add(file);
            }
            return files.Where(p => !ignored.Contains(p)).ToArray();
        }
        catch (System.ComponentModel.Win32Exception) { return files; }
    }

    public static async Task<string[]> SelectAsync(string root, string[] files, AnalysisRequest request, CancellationToken token)
    {
        if (request.Scope == "repository") return files;
        if (request.Scope == "paths") return SelectPaths(root, files, request);
        return await ChangedAsync(root, files, request, token);
    }

    private static string[] SelectPaths(string root, string[] files, AnalysisRequest request)
    {
        if (request.Paths is not { Length: > 0 }) throw new ArgumentException("Paths scope requires at least one path.");
        var prefixes = request.Paths.Select(p => Path.GetRelativePath(root, Path.GetFullPath(p, root)).Replace('\\', '/').TrimEnd('/')).ToArray();
        if (prefixes.Any(p => p == ".." || p.StartsWith("../", StringComparison.Ordinal))) throw new ArgumentException("Selected paths must be inside the project root.");
        return files.Where(p => prefixes.Any(x => x == "." || p == x || p.StartsWith(x + "/", StringComparison.Ordinal))).ToArray();
    }

    private static async Task<string[]> ChangedAsync(string root, string[] files, AnalysisRequest request, CancellationToken token)
    {
        var commands = request.GitBase is null
            ? new[] { new[] { "diff", "--relative", "--name-only", "-z", "--diff-filter=ACMRT", "--" }, new[] { "diff", "--relative", "--cached", "--name-only", "-z", "--diff-filter=ACMRT", "--" } }
            : new[] { new[] { "diff", "--relative", "--name-only", "-z", "--diff-filter=ACMRT", request.GitBase, "--" } };
        if (request.GitBase?.StartsWith('-') == true) throw new ArgumentException("Git base cannot be an option.");
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var args in commands.Append(["ls-files", "--others", "--exclude-standard", "-z", "--"]))
            foreach (var p in (await GitAsync(root, args, token)).Split('\0', StringSplitOptions.RemoveEmptyEntries))
                changed.Add(p.Replace('\\', '/'));
        return files.Where(changed.Contains).ToArray();
    }

    private static async Task<string> GitAsync(string root, string[] args, CancellationToken token)
    {
        var result = await ProcessRunner.RunAsync("git", args, root, token);
        if (result.ExitCode != 0) throw new ArgumentException("Git scope unavailable: " + result.Error.Trim());
        return result.Output;
    }
}
