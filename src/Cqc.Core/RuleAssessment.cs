namespace Cqc.Core;

internal sealed record FindingDefinition
{
    public required string Rule { get; init; }
    public required string Key { get; init; }
    public required string Message { get; init; }
    public required string Advice { get; init; }
    public required IEnumerable<string> Entities { get; init; }
    public required IEnumerable<Location> Locations { get; init; }
}

internal sealed record FindingOrigin(string TypeId, Location Location, Location? TouchRange = null);

internal sealed record LocatedFinding(Finding Finding, FindingOrigin[] Origins);

internal enum DuplicateCandidateKind { Executable, Method }

internal sealed record ScoringContribution(string TypeId, Location Location)
{
    public bool Primary { get; init; } = true;
    public bool Static { get; init; }
    public string[] Blockers { get; init; } = [];
    public string[] StaticBlockers { get; init; } = [];
    public SortedDictionary<string, double?> Metrics { get; init; } = RuleContext.Metrics();
    public DuplicateCandidateKind? DuplicateCandidate { get; init; }
}

internal sealed record FamilyAssessment
{
    public required string Family { get; init; }
    public required IEnumerable<TypeFact> Types { get; init; }
    public required IEnumerable<Finding> Findings { get; init; }
    public HashSet<string> StaticOnlyFindingIds { get; init; } = [];
    public bool Incomplete { get; init; }
    public IEnumerable<Diagnostic> Diagnostics { get; init; } = [];
    public IEnumerable<ScoringContribution> Contributions { get; init; } = [];
    public Func<TypeFact, ChangedCodeScope, bool> IncludeTouchedDeclaration { get; init; } = (_, _) => false;
    public Func<SelectedTypeEvidence, EntityScoringEvidence> Aggregate { get; init; } = evidence => evidence.Assess();
    public bool ShowOnlyTouchedFindingLocations { get; init; }
    public Func<string, FindingOrigin[]> FindingOrigins { get; init; } = _ => [];
}

internal sealed record EntityAssessment
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string File { get; init; }
    public required Finding[] Findings { get; init; }
    public required string[] TypeIds { get; init; }
}

internal sealed record EntityScoringEvidence(bool PrimaryApplies, bool StaticApplies,
    IEnumerable<string> Blockers, IEnumerable<string> StaticBlockers, SortedDictionary<string, double?> Metrics);

internal sealed record SelectedTypeEvidence(string TypeId, TypeFact[] Declarations, ScoringContribution[] Contributions,
    IEnumerable<Finding> Findings, bool Touched)
{
    internal EntityScoringEvidence Assess(bool primary = true, bool staticScore = false,
        IEnumerable<string>? declarationBlockers = null, IEnumerable<string>? staticDeclarationBlockers = null) => new(primary, staticScore,
            Contributions.Where(entry => entry.Primary).SelectMany(entry => entry.Blockers).Concat(declarationBlockers ?? []),
            Contributions.Where(entry => entry.Static).SelectMany(entry => entry.StaticBlockers).Concat(staticDeclarationBlockers ?? []),
            ContributionMetrics());

    private SortedDictionary<string, double?> ContributionMetrics()
    {
        var metrics = RuleContext.Metrics();
        foreach (var (name, value) in Contributions.SelectMany(entry => entry.Metrics))
        {
            if (Touched) metrics.TryAdd(name, value);
            else metrics[name] = value;
        }
        return metrics;
    }
}
