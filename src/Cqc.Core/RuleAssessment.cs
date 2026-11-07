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

internal sealed record FamilyAssessment
{
    public required string Family { get; init; }
    public required IEnumerable<TypeFact> Types { get; init; }
    public required IEnumerable<Finding> Findings { get; init; }
    public Func<string, bool> Complete { get; init; } = _ => true;
    public Func<string, SortedDictionary<string, double?>> Metrics { get; init; } = _ => RuleContext.Metrics();
    public IEnumerable<Diagnostic> Diagnostics { get; init; } = [];
}

internal sealed record EntityAssessment
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string File { get; init; }
    public required Finding[] Findings { get; init; }
    public required bool Complete { get; init; }
    public required SortedDictionary<string, double?> Metrics { get; init; }
}
