namespace Cqc.Core;

/// <summary>Request-local fact views, constructed only when a family needs them.</summary>
internal sealed class FactIndex
{
    private readonly AnalysisFacts facts;
    private ILookup<string, MethodFact>? fixturesByType;
    private ILookup<string, TypeFact>? typesById;
    private ILookup<string, TypeFact>? typesByContext;

    public FactIndex(AnalysisFacts facts)
    {
        this.facts = facts;
        Methods = facts.Methods.OrderBy(m => m.Id, StringComparer.Ordinal)
            .ThenBy(m => m.Location.File, StringComparer.Ordinal).ToArray();
    }

    public MethodFact[] Methods { get; }
    public ILookup<string, MethodFact> FixturesByType => fixturesByType ??=
        Methods.Where(m => m.IsFixture).ToLookup(m => m.TypeId, StringComparer.Ordinal);
    public ILookup<string, TypeFact> TypesById => typesById ??=
        facts.Types.ToLookup(t => t.Id, StringComparer.Ordinal);
    public ILookup<string, TypeFact> TypesByContext => typesByContext ??=
        facts.Types.Where(t => t.ContextId is not null).ToLookup(t => t.ContextId!, StringComparer.Ordinal);
}
