namespace Cqc.Core;

internal sealed class ChangedFindings(ChangedCodeScope scope, FactIndex? dependencyIndex = null)
{
    private readonly ILookup<(string TypeId, string File), StructuralExecutable>? dependencyOwners = dependencyIndex?.Executables
        .ToLookup(entry => (entry.TypeId, entry.Location.File));

    internal IEnumerable<LocatedFinding> Select(FamilyAssessment assessment) => assessment.Findings
        .Select(finding => Select(finding, assessment.FindingOrigins(finding.Id))).OfType<LocatedFinding>();

    private LocatedFinding? Select(Finding finding, FindingOrigin[] knownOrigins)
    {
        var origins = knownOrigins.Length != 0 ? knownOrigins : finding.EntityIds
            .SelectMany(id => finding.Locations.Select(location => new FindingOrigin(id, location)));
        var touched = origins.Where(origin => Includes(origin.TypeId, origin.Location)).ToArray();
        if (touched.Length == 0) return null;
        return new(finding with
        {
            EntityIds = touched.Select(origin => origin.TypeId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Locations = dependencyOwners is null ? finding.Locations : touched.Select(origin => origin.Location).Distinct().ToArray()
        }, touched);
    }

    internal bool Includes(string typeId, Location location)
    {
        if (dependencyOwners is null) return scope.Includes(location);
        var owner = dependencyOwners[(typeId, location.File)].Select(entry => entry.Location)
            .Where(range => range.StartLine <= location.StartLine && location.EndLine <= range.EndLine)
            .MinBy(range => range.EndLine - range.StartLine);
        return scope.Includes(owner ?? location);
    }
}
