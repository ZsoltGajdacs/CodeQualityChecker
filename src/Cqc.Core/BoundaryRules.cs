namespace Cqc.Core;

internal static class BoundaryRules
{
    internal static IEnumerable<Finding> Evaluate(DependencyFact[] edges, Dictionary<string, string> components, QualityConfig config)
        => edges.Where(edge => Violates(edge, components, config)).Select(edge => Finding(edge, components));

    private static bool Violates(DependencyFact edge, Dictionary<string, string> components, QualityConfig config)
    {
        var from = components[edge.From];
        var to = components[edge.To];
        if (from == to) return false;
        if (config.ForbiddenDependencies.Any(pair => IsForbidden(pair, edge, components))) return true;
        if (!config.AllowedDependencies.TryGetValue(from, out var allowed)) return false;
        return !allowed.Contains("*", StringComparer.Ordinal) && !allowed.Contains(to, StringComparer.Ordinal);
    }

    private static bool IsForbidden(string[] pair, DependencyFact edge, Dictionary<string, string> components)
        => pair.Length == 2 && pair[0] == components[edge.From] && pair[1] == components[edge.To];

    private static Finding Finding(DependencyFact edge, Dictionary<string, string> components)
        => RuleContext.Finding(new() { Rule = "dependencies.boundary", Key = edge.From + "|" + edge.To,
            Message = $"Dependency {edge.From} -> {edge.To} violates configured boundary {components[edge.From]} -> {components[edge.To]}.",
            Advice = "Remove or invert the dependency, or revise architectural policy after reviewing the intended boundary.",
            Entities = [edge.From], Locations = [edge.Location] });
}
