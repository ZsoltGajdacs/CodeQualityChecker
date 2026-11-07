namespace Cqc.Core;

internal static class DuplicationRules
{
    private sealed record Match(MethodFact Left, MethodFact Right, double Similarity);

    public static FamilyResult Evaluate(RuleContext context, QualityConfig config)
    {
        var eligible = context.Methods.Where(method => Eligible(method, config))
            .GroupBy(m => m.Id, StringComparer.Ordinal).Select(group => group.First()).ToArray();
        var matches = Matches(eligible, config.DuplicateSimilarity, context.CancellationToken);
        var findings = Findings(eligible, matches).ToArray();
        var incompleteLanguages = IncompleteLanguages(context.Facts);
        var types = context.Facts.Types.GroupBy(t => t.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First());
        return context.Finish(new() { Family = "duplication", Types = context.Facts.Types, Findings = findings,
            Complete = id => !incompleteLanguages.Contains(types[id].Language),
            Metrics = id => new(StringComparer.Ordinal)
            {
                ["eligibleMethods"] = eligible.Count(m => m.TypeId == id),
                ["duplicateGroups"] = findings.Count(f => f.EntityIds.Contains(id, StringComparer.Ordinal))
            } });
    }

    private static bool Eligible(MethodFact method, QualityConfig config)
        => method.Location.EndLine - method.Location.StartLine + 1 >= config.DuplicateMinLines
            && method.NodeCount >= config.DuplicateMinNodes && method.Fingerprints.Length > 0;

    private static HashSet<string> IncompleteLanguages(AnalysisFacts facts)
        => facts.Methods.Where(m => !m.Complete).Select(m => m.Language)
            .Concat(facts.Types.Where(t => !t.Complete).Select(t => t.Language))
            .Concat(facts.Diagnostics.Where(d => d.Code is "analyzer_failed" or "java.parse").Select(d => d.Language).OfType<string>())
            .ToHashSet(StringComparer.Ordinal);

    private static Match[] Matches(MethodFact[] methods, double threshold, CancellationToken cancellationToken)
    {
        var fingerprints = methods.ToDictionary(method => method.Id,
            method => method.Fingerprints.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        return methods.GroupBy(method => method.Language, StringComparer.Ordinal).SelectMany(Pairs)
            .Select(pair => Compare(pair, fingerprints, cancellationToken)).Where(match => match.Similarity >= threshold).ToArray();
    }

    private static IEnumerable<(MethodFact Left, MethodFact Right)> Pairs(IGrouping<string, MethodFact> group)
    {
        var methods = group.ToArray();
        return methods.SelectMany((left, index) => methods.Skip(index + 1).Select(right => (left, right)));
    }

    private static Match Compare((MethodFact Left, MethodFact Right) pair, Dictionary<string, HashSet<string>> fingerprints, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new(pair.Left, pair.Right, Jaccard(fingerprints[pair.Left.Id], fingerprints[pair.Right.Id]));
    }

    private static IEnumerable<Finding> Findings(MethodFact[] methods, Match[] matches)
    {
        var byId = methods.ToDictionary(method => method.Id, StringComparer.Ordinal);
        var links = matches.Select(match => (match.Left.Id, match.Right.Id));
        return GraphAlgorithms.Components(byId.Keys, links, false).Where(group => group.Length > 1)
            .Select(group => GroupFinding(group, byId, matches));
    }

    private static Finding GroupFinding(string[] group, Dictionary<string, MethodFact> byId, Match[] matches)
    {
        var members = group.Select(id => byId[id]).ToArray();
        var minimum = matches.Where(match => group.Contains(match.Left.Id, StringComparer.Ordinal)
            && group.Contains(match.Right.Id, StringComparer.Ordinal)).Min(match => match.Similarity);
        return RuleContext.Finding(new() { Rule = "duplication.group", Key = string.Join("|", group),
            Message = FormattableString.Invariant($"Structural duplicate group with {group.Length} methods; minimum qualifying pair similarity {minimum:0.###}."),
            Advice = "Inspect all locations and extract shared behavior only when it improves cohesion; parameterized test cases may be intentional.",
            Entities = members.Select(method => method.TypeId), Locations = members.Select(method => method.Location) });
    }

    private static double Jaccard(HashSet<string> left, HashSet<string> right)
    {
        var shared = left.Count(right.Contains);
        var total = left.Count + right.Count - shared;
        return total == 0 ? 0 : (double)shared / total;
    }
}
