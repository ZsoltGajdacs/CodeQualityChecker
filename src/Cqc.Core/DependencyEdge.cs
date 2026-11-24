namespace Cqc.Core;

internal sealed record DependencyEdge(string From, string To, FindingOrigin[] References)
{
    internal Location Representative => References[0].Location;

    internal static DependencyEdge[] Group(RuleContext context, Dictionary<string, TypeFact> types)
    {
        var references = context.Facts.Dependencies.Where(edge => edge.From != edge.To
            && types.ContainsKey(edge.From) && types.ContainsKey(edge.To));
        var owners = context.ChangedCode is null ? null : context.Index.Executables
            .ToLookup(entry => (entry.TypeId, entry.Location.File), entry => entry.Location);
        return references.GroupBy(edge => (edge.From, edge.To))
            .OrderBy(group => group.Key.From, StringComparer.Ordinal).ThenBy(group => group.Key.To, StringComparer.Ordinal)
            .Select(group => new DependencyEdge(group.Key.From, group.Key.To, group.Select(edge => edge.Location).Distinct()
                .Select(location => Origin(group.Key.From, location, owners?[(group.Key.From, location.File)] ?? [])).ToArray()))
            .ToArray();
    }

    private static FindingOrigin Origin(string from, Location reference, IEnumerable<Location> owners)
    {
        var range = owners.Where(owner => owner.StartLine <= reference.StartLine && owner.EndLine >= reference.EndLine)
            .MinBy(owner => owner.EndLine - owner.StartLine);
        return new(from, reference, range);
    }
}
