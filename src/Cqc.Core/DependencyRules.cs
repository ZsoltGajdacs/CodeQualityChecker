namespace Cqc.Core;

internal static class DependencyRules
{
    public static FamilyResult Evaluate(RuleContext context, QualityConfig config)
    {
        var evidence = new DependencyEvidence(context.Facts, context.Index);
        var types = context.Index.TypesById
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var edges = DependencyEdge.Group(context, types);
        var findings = CycleFindings(types, edges).ToList();
        var components = types.ToDictionary(p => p.Key, p => ComponentAssignment.Resolve(p.Value, config), StringComparer.Ordinal);
        findings.AddRange(ComponentCycles(edges, components, findings));
        findings.AddRange(BoundaryRules.Evaluate(edges, components, config));
        var origins = findings.ToDictionary(entry => entry.Finding.Id, entry => entry.Origins, StringComparer.Ordinal);
        var metrics = context.ChangedCode is null ? DependencyMetrics.Evaluate(types, edges, components) : null;
        var diagnostics = context.Facts.SemanticComplete ? Array.Empty<Diagnostic>() :
            [new Diagnostic("dependencies.incomplete", "Symbol resolution is incomplete. Resolve language diagnostics or execute a supported build before architecture scoring.")];
        return context.Finish(new() { Family = "dependencies", Types = context.Facts.Types, Findings = findings.Select(entry => entry.Finding),
            Diagnostics = diagnostics, ShowOnlyTouchedFindingLocations = true,
            IncludeTouchedDeclaration = (type, scope) => scope.IsWholeFile(type.Location.File),
            Aggregate = selected => selected.Assess(
                declarationBlockers: selected.Declarations.SelectMany(evidence.Blockers))
                with { Metrics = metrics?[components[selected.TypeId]] ?? RuleContext.Metrics() },
            FindingOrigins = id => origins[id],
            Contributions = context.ChangedCode is null ? [] : Contributions(context.Index, edges) });
    }

    private static IEnumerable<ScoringContribution> Contributions(FactIndex index, DependencyEdge[] edges) =>
        index.Executables.Select(executable => new ScoringContribution(executable.TypeId, executable.Location))
            .Concat(edges.SelectMany(edge => edge.References)
                .Select(origin => new ScoringContribution(origin.TypeId, origin.TouchRange ?? origin.Location)));

    private static IEnumerable<LocatedFinding> ComponentCycles(DependencyEdge[] edges, Dictionary<string, string> components, List<LocatedFinding> existing)
    {
        var scored = existing.SelectMany(f => f.Finding.EntityIds).ToHashSet(StringComparer.Ordinal);
        var links = edges.Select(e => (From: components[e.From], To: components[e.To])).Where(e => e.From != e.To);
        foreach (var cycle in GraphAlgorithms.Components(components.Values, links, true).Where(c => c.Length > 1))
        {
            var involved = edges.Where(e => cycle.Contains(components[e.From], StringComparer.Ordinal)
                && cycle.Contains(components[e.To], StringComparer.Ordinal));
            foreach (var group in involved.GroupBy(e => e.From, StringComparer.Ordinal).Where(g => !scored.Contains(g.Key)))
                yield return Locate(new() { Rule = "dependencies.cycle", Key = group.Key + "|components:" + string.Join("|", cycle),
                    Message = "Class participates in an architectural component cycle: " + string.Join(" -> ", cycle) + ".",
                    Advice = "Break the component cycle by moving shared policy or inverting dependencies at the boundary.",
                    Entities = [group.Key], Locations = group.Select(e => e.Representative) }, group);
        }
    }

    private static IEnumerable<LocatedFinding> CycleFindings(Dictionary<string, TypeFact> types, DependencyEdge[] edges)
    {
        foreach (var cycle in GraphAlgorithms.Components(types.Keys, edges.Select(e => (e.From, e.To)), true).Where(c => c.Length > 1))
            foreach (var id in cycle)
            {
                var involved = edges.Where(e => e.From == id && cycle.Contains(e.To, StringComparer.Ordinal)).ToArray();
                yield return Locate(new() { Rule = "dependencies.cycle", Key = id + "|" + string.Join("|", cycle),
                    Message = "Class participates in a dependency cycle: " + string.Join(" -> ", cycle) + ".",
                    Advice = "Break the cycle by moving shared policy or introducing an interface at the architectural boundary.",
                    Entities = [id], Locations = involved.Select(e => e.Representative) }, involved);
            }
    }

    private static LocatedFinding Locate(FindingDefinition definition, IEnumerable<DependencyEdge> edges) =>
        new(RuleContext.Finding(definition), edges.SelectMany(edge => edge.References).ToArray());

}
