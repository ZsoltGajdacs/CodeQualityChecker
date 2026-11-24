namespace Cqc.Core;

internal static class DuplicationRules
{
    public static FamilyResult Evaluate(RuleContext context, QualityConfig config)
    {
        var minLines = Math.Max(QualityConfig.MinimumDuplicateLines, config.DuplicateMinLines);
        var eligible = context.Index.Executables.Where(executable => Eligible(executable, minLines, config.DuplicateMinNodes))
            .DistinctBy(executable => executable.Id, StringComparer.Ordinal).ToArray();
        var matches = new DuplicateCandidates(eligible, context.CancellationToken).Matches(config.DuplicateSimilarity).ToArray();
        var groups = Findings(eligible, matches).ToArray();
        var findings = groups.Select(group => group.Finding).ToArray();
        var origins = groups.ToDictionary(group => group.Finding.Id, group => group.Origins, StringComparer.Ordinal);
        var incompleteTypes = context.Facts.Types.Where(type => context.Structure.IncompleteLanguages.Contains(type.Language))
            .Select(type => type.Id).ToHashSet(StringComparer.Ordinal);
        return context.Finish(new()
        {
            Family = "duplication",
            Types = context.Facts.Types,
            Findings = findings,
            Incomplete = context.Structure.HasIncompleteSelectedLanguage,
            Contributions = Contributions(context.Index.Executables, eligible),
            IncludeTouchedDeclaration = (type, scope) => scope.IsWholeFile(type.Location.File),
            Aggregate = evidence => evidence.Assess(
                declarationBlockers: incompleteTypes.Contains(evidence.TypeId) ? ["structure.incomplete"] : [])
                with { Metrics = Metrics(evidence.Contributions, evidence.Findings.Count()) },
            FindingOrigins = id => origins[id]
        });
    }

    private static IEnumerable<ScoringContribution> Contributions(StructuralExecutable[] executables, StructuralExecutable[] eligible)
    {
        var candidates = eligible.ToHashSet();
        foreach (var executable in executables)
            yield return new(executable.TypeId, executable.Location)
            {
                DuplicateCandidate = candidates.Remove(executable)
                    ? executable.IsMethod ? DuplicateCandidateKind.Method : DuplicateCandidateKind.Executable : null
            };
    }

    private static SortedDictionary<string, double?> Metrics(ScoringContribution[] entries, int groups) =>
        new(StringComparer.Ordinal)
        {
            ["eligibleMethods"] = entries.Count(entry => entry.DuplicateCandidate == DuplicateCandidateKind.Method),
            ["eligibleExecutables"] = entries.Count(entry => entry.DuplicateCandidate is not null),
            ["duplicateGroups"] = groups
        };

    private static bool Eligible(StructuralExecutable executable, int minLines, int minNodes)
        => executable.Syntax && executable.DuplicationLines >= minLines
            && executable.NodeCount >= minNodes && executable.Fingerprints.Length > 0;

    private static IEnumerable<LocatedFinding> Findings(StructuralExecutable[] executables, DuplicateMatch[] matches)
    {
        var byId = executables.ToDictionary(executable => executable.Id, StringComparer.Ordinal);
        var links = matches.Select(match => (match.Left.Id, match.Right.Id));
        var groups = GraphAlgorithms.Components(byId.Keys, links, false).Where(group => group.Length > 1).ToArray();
        var owners = groups.SelectMany((group, index) => group.Select(id => (id, index)))
            .ToDictionary(entry => entry.id, entry => entry.index, StringComparer.Ordinal);
        var minimums = matches.GroupBy(match => owners[match.Left.Id]).ToDictionary(group => group.Key, group => group.Min(match => match.Similarity));
        return groups.Select((group, index) => GroupFinding(group, byId, minimums[index]));
    }

    private static LocatedFinding GroupFinding(string[] group, Dictionary<string, StructuralExecutable> byId, double minimum)
    {
        var members = group.Select(id => byId[id]).ToArray();
        var kind = members.All(executable => executable.IsMethod) ? "methods" : "executable bodies";
        var finding = RuleContext.Finding(new() { Rule = "duplication.group", Key = string.Join("|", group),
            Message = FormattableString.Invariant($"Structural duplicate group with {group.Length} {kind}; minimum qualifying pair similarity {minimum:0.###}."),
            Advice = "Inspect all locations and extract shared behavior only when it improves cohesion; parameterized test cases may be intentional.",
            Entities = members.Select(executable => executable.TypeId), Locations = members.Select(executable => executable.Location) });
        return new(finding, members.Select(executable => new FindingOrigin(executable.TypeId, executable.Location)).ToArray());
    }

}
