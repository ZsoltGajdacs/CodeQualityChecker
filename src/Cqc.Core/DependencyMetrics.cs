namespace Cqc.Core;

internal static class DependencyMetrics
{
    internal static Dictionary<string, SortedDictionary<string, double?>> Evaluate(
        Dictionary<string, TypeFact> types, DependencyFact[] edges, Dictionary<string, string> components)
    {
        var links = edges.Select(e => (From: components[e.From], To: components[e.To]))
            .Where(e => e.From != e.To).Distinct().ToArray();
        return types.Values.GroupBy(t => components[t.Id], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Measure(group, links), StringComparer.Ordinal);
    }

    private static SortedDictionary<string, double?> Measure(IGrouping<string, TypeFact> group, (string From, string To)[] links)
    {
        var input = links.Count(e => e.To == group.Key);
        var output = links.Count(e => e.From == group.Key);
        var instability = input + output == 0 ? 0 : (double)output / (input + output);
        var abstractness = (double)group.Count(t => t.IsAbstract) / group.Count();
        return new(StringComparer.Ordinal)
        {
            ["componentFanIn"] = input, ["componentFanOut"] = output,
            ["componentInstability"] = instability, ["componentAbstractness"] = abstractness,
            ["componentDistance"] = Math.Abs(abstractness + instability - 1),
            ["componentZone"] = Zone(abstractness + instability)
        };
    }

    private static int Zone(double abstractionAndInstability)
    {
        if (abstractionAndInstability < .7) return -1;
        return abstractionAndInstability > 1.3 ? 1 : 0;
    }
}
