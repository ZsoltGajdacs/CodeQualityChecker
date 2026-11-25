using System.Globalization;

namespace Cqc.Core;

public sealed record HotspotHistory(string Head, DateTimeOffset FromUtc, DateTimeOffset ThroughUtc);

internal sealed record GitHotspotEvidence(HotspotHistory? Window, IReadOnlyDictionary<string, HashSet<string>> Edits,
    Diagnostic? Diagnostic = null);

internal static class GitHotspotHistory
{
    internal static async Task<GitHotspotEvidence> ReadAsync(ProjectSnapshot snapshot, ChangedCodeScope? changedCode,
        CancellationToken token)
    {
        HotspotHistory? window = null;
        try
        {
            var head = changedCode is null ? await LocalGit.ReadHeadAsync(snapshot, token) : changedCode.Revision;
            if (head is null) return Unavailable(null, "The Git repository has no HEAD commit.");
            var root = (await LocalGit.OutputAsync(snapshot.Root, token, "rev-parse", "--show-toplevel")).TrimEnd('\r', '\n');
            var date = await LocalGit.OutputAsync(root, token, "show", "-s", "--no-show-signature", "--format=%ct", head);
            var through = DateTimeOffset.FromUnixTimeSeconds(long.Parse(date, CultureInfo.InvariantCulture));
            window = new(head, through.AddMonths(-12), through);
            if ((await LocalGit.OutputAsync(root, token, "rev-parse", "--is-shallow-repository")).Trim() == "true")
                return Unavailable(window, "Hotspot scoring requires a complete local Git history. The repository is shallow.");

            var tree = await LocalGit.OutputAsync(root, token, "ls-tree", "-r", "-z", "--name-only", head);
            var prefix = (await LocalGit.OutputAsync(snapshot.Root, token, "rev-parse", "--show-prefix")).TrimEnd('\r', '\n');
            var index = new GitHotspotIndex(snapshot.Files, prefix, tree);
            if (index.HasTrackedFiles)
            {
                var range = await ReadRangeAsync(root, window, token);
                var log = await LocalGit.OutputAsync(root, token, ["log", "--first-parent", "--diff-merges=first-parent", "--root",
                    "--no-ext-diff", "--no-textconv", "--no-color", "--no-relative", "--no-show-signature", "--find-renames=50%", "-l0",
                    "--format=%x00%H%x00%ct", "--name-status", "-z", .. range, "--"]);
                index.Read(log, window, token);
            }
            return new(window, index.Edits);
        }
        catch (Exception error) when (error is ArgumentException or IOException or FormatException
            or OverflowException or System.ComponentModel.Win32Exception)
        {
            return Unavailable(window, "Unable to read local Git history for hotspot scoring. " + error.Message);
        }
    }

    private static GitHotspotEvidence Unavailable(HotspotHistory? window, string reason) =>
        new(window, new Dictionary<string, HashSet<string>>(StringComparer.Ordinal), new("hotspots.history_unavailable", reason));

    private static async Task<string[]> ReadRangeAsync(string root, HotspotHistory window, CancellationToken token)
    {
        var since = window.FromUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var metadata = await LocalGit.OutputAsync(root, token, "log", "--first-parent", "--no-show-signature", "--format=%ct %P", $"--since-as-filter=@{since}",
            window.Head, "--");
        var latest = window.ThroughUtc.ToUnixTimeSeconds();
        string? before = null;
        // Keep intervening commits in the diff scan so clock skew cannot hide a rename or path recreation.
        foreach (var line in metadata.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (long.Parse(fields[0], CultureInfo.InvariantCulture) <= latest)
                before = fields.Length > 1 ? fields[1] : null;
        }
        return before is null ? [window.Head] : [window.Head, "^" + before];
    }
}
