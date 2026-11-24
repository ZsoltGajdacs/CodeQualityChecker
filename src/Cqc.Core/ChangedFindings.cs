namespace Cqc.Core;

internal sealed class ChangedFindings(ChangedCodeScope scope)
{
    internal IEnumerable<LocatedFinding> Select(FamilyAssessment assessment) => assessment.Findings
        .Select(finding => Select(finding, assessment.FindingOrigins(finding.Id), assessment.ShowOnlyTouchedFindingLocations)).OfType<LocatedFinding>();

    private LocatedFinding? Select(Finding finding, FindingOrigin[] knownOrigins, bool trimLocations)
    {
        var origins = knownOrigins.Length != 0 ? knownOrigins : finding.EntityIds
            .SelectMany(id => finding.Locations.Select(location => new FindingOrigin(id, location)));
        var touched = origins.Where(origin => scope.Includes(origin.TouchRange ?? origin.Location)).ToArray();
        if (touched.Length == 0) return null;
        return new(finding with
        {
            EntityIds = touched.Select(origin => origin.TypeId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Locations = trimLocations ? OrderedLocations(touched) : finding.Locations
        }, touched);
    }

    private static Location[] OrderedLocations(IEnumerable<FindingOrigin> origins) => origins
        .Select(origin => origin.Location).Distinct().OrderBy(location => location.File, StringComparer.Ordinal)
        .ThenBy(location => location.StartLine).ThenBy(location => location.EndLine).ToArray();
}
