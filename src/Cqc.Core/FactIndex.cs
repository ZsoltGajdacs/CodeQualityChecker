namespace Cqc.Core;

/// <summary>Request-local fact views, constructed only when a family needs them.</summary>
internal sealed class FactIndex
{
    private readonly AnalysisFacts facts;
    private ILookup<string, MethodFact>? fixturesByType;
    private ILookup<string, TypeFact>? typesById;
    private ILookup<string, TypeFact>? typesByContext;
    private StructuralExecutable[]? executables;

    public FactIndex(AnalysisFacts facts)
    {
        this.facts = facts;
        Methods = facts.Methods.OrderBy(m => m.Id, StringComparer.Ordinal)
            .ThenBy(m => m.Location.File, StringComparer.Ordinal).ToArray();
    }

    public MethodFact[] Methods { get; }
    public StructuralExecutable[] Executables => executables ??= Methods.Select(StructuralExecutable.FromMethod)
        .Concat(facts.Executables.Select(StructuralExecutable.FromInitializer)).ToArray();
    public ILookup<string, MethodFact> FixturesByType => fixturesByType ??=
        Methods.Where(m => m.IsFixture).ToLookup(m => m.TypeId, StringComparer.Ordinal);
    public ILookup<string, TypeFact> TypesById => typesById ??=
        facts.Types.ToLookup(t => t.Id, StringComparer.Ordinal);
    public ILookup<string, TypeFact> TypesByContext => typesByContext ??=
        facts.Types.Where(t => t.ContextId is not null).ToLookup(t => t.ContextId!, StringComparer.Ordinal);
}

internal sealed record StructuralExecutable
{
    public required string Id { get; init; }
    public required string TypeId { get; init; }
    public required string Language { get; init; }
    public required Location Location { get; init; }
    public int Complexity { get; init; }
    public string[] Fingerprints { get; init; } = [];
    public int NodeCount { get; init; }
    public int? DuplicationLines { get; init; }
    public bool Syntax { get; init; }
    public bool IsMethod { get; init; }

    internal static StructuralExecutable FromMethod(MethodFact method) =>
        new()
        {
            Id = method.Id, TypeId = method.TypeId, Language = method.Language, Location = method.Location,
            Complexity = method.Complexity, Fingerprints = method.Fingerprints, NodeCount = method.NodeCount,
            DuplicationLines = method.DuplicationLines, Syntax = AssessmentEvidence.Of(method).Syntax, IsMethod = true
        };

    internal static StructuralExecutable FromInitializer(ExecutableFact executable) =>
        new()
        {
            Id = executable.Id, TypeId = executable.TypeId, Language = executable.Language, Location = executable.Location,
            Complexity = executable.Complexity, Fingerprints = executable.Fingerprints, NodeCount = executable.NodeCount,
            DuplicationLines = executable.DuplicationLines, Syntax = true
        };
}
