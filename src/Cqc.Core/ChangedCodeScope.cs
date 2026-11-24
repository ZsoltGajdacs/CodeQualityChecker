using System.Globalization;
using System.Text.RegularExpressions;

namespace Cqc.Core;

/// <summary>Working-tree source ranges compared with the current commit.</summary>
internal sealed class ChangedCodeScope
{
    private static readonly Regex HunkHeader = new(@"^@@ -[0-9]+(?:,[0-9]+)? \+([0-9]+)(?:,([0-9]+))? @@",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private readonly string? revision;
    private readonly IReadOnlyDictionary<string, ChangedFile> files;

    private ChangedCodeScope(string? revision, IReadOnlyDictionary<string, ChangedFile> files)
    {
        this.revision = revision;
        this.files = files;
    }

    internal bool Includes(Location location) => files.TryGetValue(location.File, out var file)
        && (file.WholeFile || file.Hunks.Any(hunk => hunk.Touches(location)));

    internal bool IsWholeFile(string file) => files.TryGetValue(file, out var change) && change.WholeFile;

    internal static async Task VerifyAsync(ChangedCodeScope? scope, ProjectSnapshot snapshot, CancellationToken token)
    {
        if (scope is not null && !scope.Matches(await ReadAsync(snapshot, token)))
            throw new InvalidOperationException("Git scope changed during analysis. No scores are accepted; rerun against a stable snapshot.");
    }

    private bool Matches(ChangedCodeScope other) => revision == other.revision && files.Count == other.files.Count
        && files.All(entry => other.files.TryGetValue(entry.Key, out var change) && entry.Value.Matches(change));

    internal static async Task<ChangedCodeScope> ReadAsync(ProjectSnapshot snapshot, CancellationToken token)
    {
        var head = await ReadHeadAsync(snapshot, token);
        var changes = snapshot.SelectedFiles.ToDictionary(path => path, _ => ChangedFile.New, StringComparer.Ordinal);
        if (head is null || changes.Count == 0) return new(head, changes);

        var baseline = await ReadBaselinePathsAsync(snapshot, head, token);
        foreach (var path in snapshot.SelectedFiles.Where(baseline.Contains))
            changes[path] = new(false, await ReadHunksAsync(snapshot, head, path, token));

        return new(head, changes);
    }

    private static async Task<string?> ReadHeadAsync(ProjectSnapshot snapshot, CancellationToken token)
    {
        var result = await ProcessRunner.RunAsync("git", ["rev-parse", "--verify", "--quiet", "HEAD^{commit}"], snapshot.Root, token);
        return result.ExitCode == 1 ? null : GitScope.OutputOrThrow(result).Trim();
    }

    private static async Task<HashSet<string>> ReadBaselinePathsAsync(ProjectSnapshot snapshot, string head, CancellationToken token)
    {
        var output = await GitScope.GitAsync(snapshot.Root, ["ls-tree", "-r", "-z", "--name-only", head, "--", "."], token);
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<LineHunk[]> ReadHunksAsync(ProjectSnapshot snapshot, string head, string path, CancellationToken token)
    {
        // Compare the blob to the physical file even when the live index no longer has this path.
        var patch = await GitScope.GitAsync(snapshot.Root, ["--literal-pathspecs", "diff", "--relative", "--no-renames",
            "--no-ext-diff", "--no-textconv", "--no-color", "--text", "--unified=0", "--inter-hunk-context=0",
            "--output-indicator-new=+", "--output-indicator-old=-", "--output-indicator-context= ",
            $"{head}:./{path}", "--", path], token);
        using var reader = new StringReader(patch);
        var hunks = new List<LineHunk>();
        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith("@@ ", StringComparison.Ordinal)) continue;
            var match = HunkHeader.Match(line);
            if (!match.Success) throw new ArgumentException("Git scope unavailable: invalid diff hunk.");
            var start = int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
            var count = match.Groups[2].Success ? int.Parse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture) : 1;
            hunks.Add(new(start, count));
        }

        return hunks.ToArray();
    }

    private sealed record ChangedFile(bool WholeFile, LineHunk[] Hunks)
    {
        internal static readonly ChangedFile New = new(true, []);
        internal bool Matches(ChangedFile other) => WholeFile == other.WholeFile && Hunks.SequenceEqual(other.Hunks);
    }

    private readonly record struct LineHunk(int Start, int Count)
    {
        // A deletion-only hunk is a gap after Start, which must be inside the current construct.
        internal bool Touches(Location location) => Start <= location.EndLine && (Count == 0
            ? location.StartLine <= Start && Start < location.EndLine
            : location.StartLine < (long)Start + Count);
    }
}
