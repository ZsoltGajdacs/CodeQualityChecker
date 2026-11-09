namespace Cqc.Core;

internal static class DependencyMetrics
{
    internal static Dictionary<string, SortedDictionary<string, double?>> Evaluate(
        Dictionary<string, TypeFact> types, DependencyFact[] edges, Dictionary<string, string> components)
    {
        var links = edges.Select(e => (From: components[e.From], To: components[e.To]))
            .Where(e => e.From != e.To).Distinct().ToArray();
        var inputs = links.ToLookup(e => e.To, StringComparer.Ordinal);
        var outputs = links.ToLookup(e => e.From, StringComparer.Ordinal);
        return types.Values.GroupBy(t => components[t.Id], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Measure(group, inputs[group.Key].Count(), outputs[group.Key].Count()), StringComparer.Ordinal);
    }

    private static SortedDictionary<string, double?> Measure(IGrouping<string, TypeFact> group, int input, int output)
    {
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
