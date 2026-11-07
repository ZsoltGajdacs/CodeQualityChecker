namespace Cqc.Core;

internal static class GraphAlgorithms
{
    public static string[][] Components(IEnumerable<string> nodes, IEnumerable<(string From, string To)> edges, bool directed)
    {
        var ordered = nodes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var graph = Build(ordered, edges, directed);
        var reverse = Build(ordered, graph.SelectMany(p => p.Value.Select(to => (to, p.Key))), true);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        return FinishOrder(ordered, graph).AsEnumerable().Reverse().Where(root => !visited.Contains(root))
            .Select(root => Collect(root, reverse, visited))
            .OrderBy(group => group[0], StringComparer.Ordinal).ToArray();
    }

    private static Dictionary<string, string[]> Build(string[] nodes, IEnumerable<(string From, string To)> edges, bool directed)
    {
        var known = nodes.ToHashSet(StringComparer.Ordinal);
        var valid = edges.Where(e => known.Contains(e.From) && known.Contains(e.To)).ToArray();
        var links = directed ? valid : valid.Concat(valid.Select(e => (From: e.To, To: e.From)));
        var groups = links.ToLookup(e => e.From, e => e.To, StringComparer.Ordinal);
        return nodes.ToDictionary(node => node, node => groups[node].Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
    }

    private static string[] Collect(string root, Dictionary<string, string[]> graph, HashSet<string> visited)
    {
        var pending = new Stack<string>();
        var result = new List<string>();
        visited.Add(root);
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            result.Add(node);
            foreach (var next in graph[node].Where(visited.Add)) pending.Push(next);
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    private static List<string> FinishOrder(string[] nodes, Dictionary<string, string[]> graph)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var root in nodes.Where(node => !visited.Contains(node))) Visit(root, graph, visited, result);
        return result;
    }

    private static void Visit(string root, Dictionary<string, string[]> graph, HashSet<string> visited, List<string> result)
    {
        var pending = new Stack<(string Node, bool Exit)>();
        pending.Push((root, false));
        while (pending.TryPop(out var frame))
        {
            if (frame.Exit) { result.Add(frame.Node); continue; }
            if (!visited.Add(frame.Node)) continue;
            pending.Push((frame.Node, true));
            foreach (var next in graph[frame.Node].Reverse().Where(node => !visited.Contains(node)))
                pending.Push((next, false));
        }
    }
}
