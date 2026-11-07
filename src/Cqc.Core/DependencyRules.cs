using System.IO.Enumeration;

namespace Cqc.Core;

internal static class DependencyRules
{
    public static FamilyResult Evaluate(RuleContext context, QualityConfig config)
    {
        var types = context.Facts.Types.GroupBy(t => t.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var edges = context.Facts.Dependencies.Where(d => types.ContainsKey(d.From) && types.ContainsKey(d.To)
            && d.From != d.To).DistinctBy(d => (d.From, d.To)).OrderBy(d => d.From, StringComparer.Ordinal)
            .ThenBy(d => d.To, StringComparer.Ordinal).ToArray();
        var findings = CycleFindings(types, edges).ToList();
        var components = types.ToDictionary(p => p.Key, p => Component(p.Value, config), StringComparer.Ordinal);
        findings.AddRange(ComponentCycles(edges, components, findings));
        findings.AddRange(BoundaryRules.Evaluate(edges, components, config));
        var metrics = DependencyMetrics.Evaluate(types, edges, components);
        var diagnostics = context.Facts.SemanticComplete ? Array.Empty<Diagnostic>() :
            [new Diagnostic("dependencies.incomplete", "Symbol resolution is incomplete. Resolve language diagnostics or execute a supported build before architecture scoring.")];
        return context.Finish(new() { Family = "dependencies", Types = context.Facts.Types, Findings = findings,
            Complete = id => SemanticComplete(context.Facts, types[id].Language), Metrics = id => metrics[components[id]], Diagnostics = diagnostics });
    }

    private static bool SemanticComplete(AnalysisFacts facts, string language)
    {
        if (facts.SemanticComplete) return true;
        if (facts.Diagnostics.Length == 0) return false;
        return facts.Diagnostics.All(d => d.Language is not null && d.Language != language);
    }

    private static IEnumerable<Finding> ComponentCycles(DependencyFact[] edges, Dictionary<string, string> components, List<Finding> existing)
    {
        var scored = existing.SelectMany(f => f.EntityIds).ToHashSet(StringComparer.Ordinal);
        var links = edges.Select(e => (From: components[e.From], To: components[e.To])).Where(e => e.From != e.To);
        foreach (var cycle in GraphAlgorithms.Components(components.Values, links, true).Where(c => c.Length > 1))
        {
            var involved = edges.Where(e => cycle.Contains(components[e.From], StringComparer.Ordinal)
                && cycle.Contains(components[e.To], StringComparer.Ordinal));
            foreach (var group in involved.GroupBy(e => e.From, StringComparer.Ordinal).Where(g => !scored.Contains(g.Key)))
                yield return RuleContext.Finding(new() { Rule = "dependencies.cycle", Key = group.Key + "|components:" + string.Join("|", cycle),
                    Message = "Class participates in an architectural component cycle: " + string.Join(" -> ", cycle) + ".",
                    Advice = "Break the component cycle by moving shared policy or inverting dependencies at the boundary.",
                    Entities = [group.Key], Locations = group.Select(e => e.Location) });
        }
    }

    private static IEnumerable<Finding> CycleFindings(Dictionary<string, TypeFact> types, DependencyFact[] edges)
    {
        foreach (var cycle in GraphAlgorithms.Components(types.Keys, edges.Select(e => (e.From, e.To)), true).Where(c => c.Length > 1))
            foreach (var id in cycle)
                yield return RuleContext.Finding(new() { Rule = "dependencies.cycle", Key = id + "|" + string.Join("|", cycle),
                    Message = "Class participates in a dependency cycle: " + string.Join(" -> ", cycle) + ".",
                    Advice = "Break the cycle by moving shared policy or introducing an interface at the architectural boundary.",
                    Entities = [id], Locations = edges.Where(e => e.From == id && cycle.Contains(e.To, StringComparer.Ordinal)).Select(e => e.Location) });
    }

    private static string Component(TypeFact type, QualityConfig config)
    {
        foreach (var component in config.Components)
            if (component.Value.Any(pattern => Matches(QualifiedName(type.Id, pattern), pattern))) return component.Key;
        var name = QualifiedName(type.Id, "");
        var separator = name.LastIndexOf('.');
        return type.Language + ":" + (separator < 0 ? "<global>" : name[..separator]);
    }

    private static string QualifiedName(string id, string pattern)
    {
        if (pattern.Contains(':')) return id;
        var separator = id.IndexOf(':');
        return separator < 0 ? id : id[(separator + 1)..];
    }

    private static bool Matches(string id, string pattern) => pattern.Contains('*') || pattern.Contains('?')
        ? FileSystemName.MatchesSimpleExpression(pattern, id, ignoreCase: false)
        : id == pattern || id.StartsWith(pattern + ".", StringComparison.Ordinal);

}
