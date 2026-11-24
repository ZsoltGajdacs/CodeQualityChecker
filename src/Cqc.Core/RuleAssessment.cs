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

internal sealed record FindingOrigin(string TypeId, Location Location);

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
    public Func<string, string[]> Blockers { get; init; } = _ => [];
    public Func<string, string[]>? StaticBlockers { get; init; }
    public HashSet<string> StaticOnlyFindingIds { get; init; } = [];
    public Func<string, bool> PrimaryScoreApplies { get; init; } = _ => true;
    public bool Incomplete { get; init; }
    public Func<string, SortedDictionary<string, double?>> Metrics { get; init; } = _ => RuleContext.Metrics();
    public IEnumerable<Diagnostic> Diagnostics { get; init; } = [];
    public IEnumerable<ScoringContribution> Contributions { get; init; } = [];
    public Func<TypeFact, string[]> SharedBlockers { get; init; } = _ => [];
    public Func<TypeFact, string[]> SharedStaticBlockers { get; init; } = _ => [];
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
