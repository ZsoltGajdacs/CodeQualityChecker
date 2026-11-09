namespace Cqc.Core;

/// <summary>Architecture scores require a complete reachable resolution context, not unrelated modules.</summary>
internal static class DependencyEvidence
{
    internal static string[] Blockers(AnalysisFacts facts, TypeFact type)
    {
        if (type.ContextId is not { } id) return Local(type);
        var contexts = facts.Contexts.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var reachable = Reachable(id, contexts);
        var relevant = facts.Types.Where(t => t.ContextId is not null && reachable.Contains(t.ContextId));
        var blockers = relevant.SelectMany(t => AssessmentEvidence.Reasons(
            (AssessmentEvidence.Of(t).Syntax, "syntax.incomplete"),
            (AssessmentEvidence.Of(t).Dependencies, "dependencies.unresolved"),
            (AssessmentEvidence.Of(t).VerifiedContext, "context.unverified"))).ToList();
        if (reachable.Any(key => !contexts.TryGetValue(key, out var context) || !context.Verified)) blockers.Add("context.unverified");
        blockers.AddRange(reachable.Where(contexts.ContainsKey).SelectMany(key => contexts[key].Blockers));
        if (facts.SyntaxGaps.Any(g => g.ContextId is not null && reachable.Contains(g.ContextId))) blockers.Add("syntax.incomplete");
        return blockers.Distinct().Order(StringComparer.Ordinal).ToArray();
    }

    private static string[] Local(TypeFact type)
    {
        var evidence = AssessmentEvidence.Of(type);
        return AssessmentEvidence.Reasons((evidence.Dependencies, "dependencies.unresolved"),
            (evidence.VerifiedContext, "context.unverified"), (evidence.Syntax, "syntax.incomplete"));
    }

    private static HashSet<string> Reachable(string id, Dictionary<string, AnalysisContext> contexts)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([id]);
        while (pending.TryPop(out var current))
            if (visited.Add(current) && contexts.TryGetValue(current, out var context))
                foreach (var next in context.ReachableContexts) pending.Push(next);
        return visited;
    }
}
