namespace Cqc.Core;

internal sealed record DuplicateMatch(StructuralExecutable Left, StructuralExecutable Right, double Similarity);

/// <summary>Exact Jaccard search. Postings exclude disjoint pairs; dense postings use direct comparison.</summary>
internal sealed class DuplicateCandidates(StructuralExecutable[] executables, CancellationToken token)
{
    internal long Comparisons { get; private set; }

    internal IEnumerable<DuplicateMatch> Matches(double threshold) => executables
        .GroupBy(executable => executable.Language, StringComparer.Ordinal).SelectMany(group => MatchesGroup(group.ToArray(), threshold));

    private IEnumerable<DuplicateMatch> MatchesGroup(StructuralExecutable[] members, double threshold)
    {
        token.ThrowIfCancellationRequested();
        var sets = members.Select(executable => executable.Fingerprints.ToHashSet(StringComparer.Ordinal)).ToArray();
        var postings = sets.SelectMany((set, index) => set.Select(fingerprint => (fingerprint, index)))
            .ToLookup(entry => entry.fingerprint, entry => entry.index, StringComparer.Ordinal);
        for (var left = 0; left < members.Length; left++)
        {
            token.ThrowIfCancellationRequested();
            foreach (var right in Candidates(left, sets, postings, threshold))
            {
                token.ThrowIfCancellationRequested();
                if (SizeBound(sets[left], sets[right]) < threshold) continue;
                Comparisons++;
                var shared = sets[left].Count(sets[right].Contains);
                var total = sets[left].Count + sets[right].Count - shared;
                var similarity = total == 0 ? 0 : (double)shared / total;
                if (similarity >= threshold) yield return new(members[left], members[right], similarity);
            }
        }
    }

    private IEnumerable<int> Candidates(int left, HashSet<string>[] sets, ILookup<string, int> postings, double threshold)
    {
        var remaining = sets.Length - left - 1;
        // Repeatedly merging ubiquitous postings costs more than checking a dense set directly.
        if (threshold == 0 || remaining >= 32 && sets[left].Sum(fingerprint => (long)postings[fingerprint].Count()) >= 2L * remaining)
            return Enumerable.Range(left + 1, remaining);
        var candidates = new HashSet<int>();
        foreach (var fingerprint in sets[left])
            foreach (var right in postings[fingerprint])
            {
                token.ThrowIfCancellationRequested();
                if (right > left) candidates.Add(right);
            }
        return candidates.Order();
    }

    private static double SizeBound(HashSet<string> left, HashSet<string> right) =>
        (double)Math.Min(left.Count, right.Count) / Math.Max(left.Count, right.Count);

    // Used by the performance harness to measure actual exact comparisons outside timed runs.
    internal static long CountComparisons(MethodFact[] methods, double threshold, CancellationToken token)
    {
        var search = new DuplicateCandidates(methods.Select(StructuralExecutable.FromMethod).ToArray(), token);
        foreach (var match in search.Matches(threshold)) { }
        return search.Comparisons;
    }
}
