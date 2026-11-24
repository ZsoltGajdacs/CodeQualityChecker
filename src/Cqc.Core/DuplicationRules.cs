namespace Cqc.Core;

internal static class DuplicationRules
{
    public static FamilyResult Evaluate(RuleContext context, QualityConfig config)
    {
        var minLines = Math.Max(QualityConfig.MinimumDuplicateLines, config.DuplicateMinLines);
        var eligible = context.Index.Executables.Where(executable => Eligible(executable, minLines, config.DuplicateMinNodes))
            .DistinctBy(executable => executable.Id, StringComparer.Ordinal).ToArray();
        var matches = new DuplicateCandidates(eligible, context.CancellationToken).Matches(config.DuplicateSimilarity).ToArray();
        var findings = Findings(eligible, matches).ToArray();
        var executablesByType = eligible.ToLookup(executable => executable.TypeId, StringComparer.Ordinal);
        var groupCounts = findings.SelectMany(finding => finding.EntityIds).ToLookup(id => id, StringComparer.Ordinal);
        var incompleteTypes = context.Facts.Types.Where(type => context.Structure.IncompleteLanguages.Contains(type.Language))
            .Select(type => type.Id).ToHashSet(StringComparer.Ordinal);
        return context.Finish(new()
        {
            Family = "duplication",
            Types = context.Facts.Types,
            Findings = findings,
            Incomplete = context.Structure.HasIncompleteSelectedLanguage,
            Blockers = id => incompleteTypes.Contains(id) ? ["structure.incomplete"] : [],
            Metrics = id => Metrics(executablesByType[id], groupCounts[id].Count())
        });
    }

    private static SortedDictionary<string, double?> Metrics(IEnumerable<StructuralExecutable> executables, int groups) =>
        new(StringComparer.Ordinal)
        {
            ["eligibleMethods"] = executables.Count(executable => executable.IsMethod),
            ["eligibleExecutables"] = executables.Count(),
            ["duplicateGroups"] = groups
        };

    private static bool Eligible(StructuralExecutable executable, int minLines, int minNodes)
        => executable.Syntax && executable.DuplicationLines >= minLines
            && executable.NodeCount >= minNodes && executable.Fingerprints.Length > 0;

    private static IEnumerable<Finding> Findings(StructuralExecutable[] executables, DuplicateMatch[] matches)
    {
        var byId = executables.ToDictionary(executable => executable.Id, StringComparer.Ordinal);
        var links = matches.Select(match => (match.Left.Id, match.Right.Id));
        var groups = GraphAlgorithms.Components(byId.Keys, links, false).Where(group => group.Length > 1).ToArray();
        var owners = groups.SelectMany((group, index) => group.Select(id => (id, index)))
            .ToDictionary(entry => entry.id, entry => entry.index, StringComparer.Ordinal);
        var minimums = matches.GroupBy(match => owners[match.Left.Id]).ToDictionary(group => group.Key, group => group.Min(match => match.Similarity));
        return groups.Select((group, index) => GroupFinding(group, byId, minimums[index]));
    }

    private static Finding GroupFinding(string[] group, Dictionary<string, StructuralExecutable> byId, double minimum)
    {
        var members = group.Select(id => byId[id]).ToArray();
        var kind = members.All(executable => executable.IsMethod) ? "methods" : "executable bodies";
        return RuleContext.Finding(new() { Rule = "duplication.group", Key = string.Join("|", group),
            Message = FormattableString.Invariant($"Structural duplicate group with {group.Length} {kind}; minimum qualifying pair similarity {minimum:0.###}."),
            Advice = "Inspect all locations and extract shared behavior only when it improves cohesion; parameterized test cases may be intentional.",
            Entities = members.Select(executable => executable.TypeId), Locations = members.Select(executable => executable.Location) });
    }

}
