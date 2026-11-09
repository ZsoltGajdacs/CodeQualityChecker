namespace Cqc.Core;

internal static class DuplicationRules
{
    public static FamilyResult Evaluate(RuleContext context, QualityConfig config)
    {
        var eligible = context.Methods.Where(method => Eligible(method, config))
            .GroupBy(m => m.Id, StringComparer.Ordinal).Select(group => group.First()).ToArray();
        var matches = new DuplicateCandidates(eligible, context.CancellationToken).Matches(config.DuplicateSimilarity).ToArray();
        var findings = Findings(eligible, matches).ToArray();
        var incompleteLanguages = IncompleteLanguages(context.Facts);
        var methodCounts = eligible.ToLookup(method => method.TypeId, StringComparer.Ordinal);
        var groupCounts = findings.SelectMany(finding => finding.EntityIds).ToLookup(id => id, StringComparer.Ordinal);
        var types = context.Facts.Types.GroupBy(t => t.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First());
        return context.Finish(new() { Family = "duplication", Types = context.Facts.Types, Findings = findings,
            Blockers = id => incompleteLanguages.Contains(types[id].Language) ? ["structure.incomplete"] : [],
            Metrics = id => new(StringComparer.Ordinal)
            {
                ["eligibleMethods"] = methodCounts[id].Count(),
                ["duplicateGroups"] = groupCounts[id].Count()
            } });
    }

    private static bool Eligible(MethodFact method, QualityConfig config)
        => method.Location.EndLine - method.Location.StartLine + 1 >= config.DuplicateMinLines
            && method.NodeCount >= config.DuplicateMinNodes && method.Fingerprints.Length > 0;

    private static HashSet<string> IncompleteLanguages(AnalysisFacts facts)
        => facts.Methods.Where(m => !AssessmentEvidence.Of(m).Syntax).Select(m => m.Language)
            .Concat(facts.Types.Where(t => !AssessmentEvidence.Of(t).Syntax).Select(t => t.Language))
            .Concat(facts.SyntaxGaps.Select(g => g.Language))
            .Concat(facts.Diagnostics.Where(d => d.Code is "analyzer_failed" or "java.parse").Select(d => d.Language).OfType<string>())
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<Finding> Findings(MethodFact[] methods, DuplicateMatch[] matches)
    {
        var byId = methods.ToDictionary(method => method.Id, StringComparer.Ordinal);
        var links = matches.Select(match => (match.Left.Id, match.Right.Id));
        var groups = GraphAlgorithms.Components(byId.Keys, links, false).Where(group => group.Length > 1).ToArray();
        var owners = groups.SelectMany((group, index) => group.Select(id => (id, index)))
            .ToDictionary(entry => entry.id, entry => entry.index, StringComparer.Ordinal);
        var minimums = matches.GroupBy(match => owners[match.Left.Id]).ToDictionary(group => group.Key, group => group.Min(match => match.Similarity));
        return groups.Select((group, index) => GroupFinding(group, byId, minimums[index]));
    }

    private static Finding GroupFinding(string[] group, Dictionary<string, MethodFact> byId, double minimum)
    {
        var members = group.Select(id => byId[id]).ToArray();
        return RuleContext.Finding(new() { Rule = "duplication.group", Key = string.Join("|", group),
            Message = FormattableString.Invariant($"Structural duplicate group with {group.Length} methods; minimum qualifying pair similarity {minimum:0.###}."),
            Advice = "Inspect all locations and extract shared behavior only when it improves cohesion; parameterized test cases may be intentional.",
            Entities = members.Select(method => method.TypeId), Locations = members.Select(method => method.Location) });
    }

}
