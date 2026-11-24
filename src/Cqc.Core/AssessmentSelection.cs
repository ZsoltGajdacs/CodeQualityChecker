namespace Cqc.Core;

/// <summary>Selects declarations, contributions and scoring origins for each reported type or file.</summary>
internal sealed class AssessmentSelection
{
    private readonly FamilyAssessment assessment;
    private readonly bool touched;
    private readonly ILookup<string, AssessmentPart> partsByType;
    private readonly ILookup<string, AssessmentPart> partsByFile;
    private readonly ILookup<string, Finding> findingsByType;
    private readonly ILookup<(string TypeId, string File), Finding> findingsByTypeAndFile;

    private sealed record AssessmentPart(TypeFact[] Declarations, ScoringContribution[] Contributions)
    {
        internal TypeFact Type => Declarations[0];
    }

    internal AssessmentSelection(FamilyAssessment assessment, ChangedCodeScope? scope)
    {
        this.assessment = assessment;
        touched = scope is not null;
        var parts = SelectParts(assessment, scope).ToArray();
        partsByType = parts.ToLookup(part => part.Type.Id, StringComparer.Ordinal);
        partsByFile = parts.ToLookup(part => part.Type.Location.File, StringComparer.Ordinal);
        Types = parts.SelectMany(part => part.Declarations).ToArray();

        var located = (scope is null
            ? assessment.Findings.Select(finding => ReportedFinding(assessment, finding))
            : new ChangedFindings(scope).Select(assessment))
            .DistinctBy(entry => entry.Finding.Id).OrderBy(entry => entry.Finding.Id, StringComparer.Ordinal).ToArray();
        Findings = located.Select(entry => entry.Finding).ToArray();
        findingsByType = Findings.SelectMany(finding => finding.EntityIds.Select(id => (Id: id, Finding: finding)))
            .ToLookup(entry => entry.Id, entry => entry.Finding, StringComparer.Ordinal);
        findingsByTypeAndFile = located.SelectMany(entry => entry.Origins
            .Select(origin => (origin.TypeId, origin.Location.File)).Distinct().Select(key => (Key: key, entry.Finding)))
            .ToLookup(entry => entry.Key, entry => entry.Finding);
    }

    internal TypeFact[] Types { get; }
    internal Finding[] Findings { get; }

    private static LocatedFinding ReportedFinding(FamilyAssessment assessment, Finding finding)
    {
        var locations = finding.Locations.ToHashSet();
        return new(finding, assessment.OriginsFor(finding).Where(origin => locations.Contains(origin.Location)).ToArray());
    }

    internal Finding[] FindingsForFile(string file, IEnumerable<string> typeIds) => typeIds
        .SelectMany(typeId => findingsByTypeAndFile[(typeId, file)]).DistinctBy(finding => finding.Id).ToArray();

    internal EntityScoringEvidence For(EntityAssessment entity)
    {
        var byFile = entity.Kind == "file";
        var parts = byFile ? partsByFile[entity.File] : entity.TypeIds.SelectMany(id => partsByType[id]);
        var assessments = parts.GroupBy(part => part.Type.Id, StringComparer.Ordinal)
            .Select(group => (Id: group.Key, Evidence: AssessType(group.Key, group, entity))).ToArray();
        var metrics = RuleContext.Metrics();
        foreach (var (id, evidence) in assessments)
        {
            var prefix = byFile ? $"class:{id}:" : "";
            foreach (var (key, value) in evidence.Metrics) metrics.Add(prefix + key, value);
        }
        return new(assessments.Any(entry => entry.Evidence.PrimaryApplies),
            assessments.Any(entry => entry.Evidence.StaticApplies),
            assessments.Where(entry => touched || entry.Evidence.PrimaryApplies).SelectMany(entry => entry.Evidence.Blockers),
            assessments.Where(entry => entry.Evidence.StaticApplies).SelectMany(entry => entry.Evidence.StaticBlockers), metrics);
    }

    private EntityScoringEvidence AssessType(string typeId, IEnumerable<AssessmentPart> parts, EntityAssessment entity)
    {
        var findings = entity.Kind == "file" ? findingsByTypeAndFile[(typeId, entity.File)] : findingsByType[typeId];
        return assessment.Aggregate(new(typeId, parts.SelectMany(part => part.Declarations).ToArray(),
            parts.SelectMany(part => part.Contributions).ToArray(), findings, touched));
    }

    private static IEnumerable<AssessmentPart> SelectParts(FamilyAssessment assessment, ChangedCodeScope? scope)
    {
        var contributions = assessment.Contributions.Where(entry => scope?.Includes(entry.Location) ?? true)
            .ToLookup(entry => (entry.TypeId, entry.Location.File));
        return assessment.Types.GroupBy(type => (type.Id, type.Location.File))
            .Select(group => new AssessmentPart(group.ToArray(), contributions[group.Key].ToArray()))
            .Where(part => scope is null || part.Contributions.Length != 0
                || part.Declarations.Any(type => assessment.IncludeTouchedDeclaration(type, scope)));
    }
}
