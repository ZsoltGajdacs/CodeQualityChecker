using System.Globalization;

namespace Cqc.Core;

/// <summary>Walks path identities backwards from HEAD and records edits under the current paths.</summary>
internal sealed class GitHotspotIndex
{
    private readonly Dictionary<string, HashSet<string>> edits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> active = new(StringComparer.Ordinal);

    internal GitHotspotIndex(string[] paths, string prefix, string tree)
    {
        var headPaths = tree.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        foreach (var path in paths.Where(path => headPaths.Contains(prefix + path)))
        {
            var commits = new HashSet<string>(StringComparer.Ordinal);
            edits.Add(path, commits);
            active.Add(prefix + path, commits);
        }
    }

    internal bool HasTrackedFiles => active.Count != 0;
    internal IReadOnlyDictionary<string, HashSet<string>> Edits => edits;

    internal void Read(string output, HotspotHistory window, CancellationToken token)
    {
        var fields = output.Split('\0');
        var position = 0;
        while (position < fields.Length - 1 && HasTrackedFiles)
        {
            token.ThrowIfCancellationRequested();
            if (fields[position++].Length != 0 || position + 1 >= fields.Length)
                throw new FormatException("Invalid commit header from git log.");
            var commit = fields[position++];
            var date = DateTimeOffset.FromUnixTimeSeconds(long.Parse(fields[position++], CultureInfo.InvariantCulture));
            var inWindow = date >= window.FromUtc && date <= window.ThroughUtc;
            ReadChanges(fields, ref position, inWindow ? commit : null);
        }
    }

    private void ReadChanges(string[] fields, ref int position, string? commit)
    {
        var renames = new List<(string OldPath, HashSet<string> Commits)>();
        while (position < fields.Length && fields[position].Length != 0)
        {
            var status = fields[position++].TrimStart('\n');
            var path = ReadPath(fields, ref position);
            if (status.StartsWith('R'))
            {
                var destination = ReadPath(fields, ref position);
                if (!active.Remove(destination, out var commits)) continue;
                if (status != "R100") AddEdit(commits, commit);
                renames.Add((path, commits));
            }
            else ApplyPathStatus(status, path, commit);
        }
        foreach (var (oldPath, commits) in renames)
            active.Add(oldPath, commits);
    }

    private void ApplyPathStatus(string status, string path, string? commit)
    {
        switch (status)
        {
            case "A":
                active.Remove(path);
                break;
            case "M":
            case "T":
                if (active.TryGetValue(path, out var commits)) AddEdit(commits, commit);
                break;
            case "D":
                break;
            default:
                throw new FormatException("Unexpected path status from git log.");
        }
    }

    private static string ReadPath(string[] fields, ref int position)
    {
        if (position >= fields.Length || fields[position].Length == 0)
            throw new FormatException("Missing path from git log.");
        return fields[position++];
    }

    private static void AddEdit(HashSet<string> edits, string? commit)
    {
        if (commit is not null) edits.Add(commit);
    }
}
