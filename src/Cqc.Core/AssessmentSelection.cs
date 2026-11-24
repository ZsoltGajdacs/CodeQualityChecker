namespace Cqc.Core;

/// <summary>Selects declarations, contributions and scoring origins for each reported type or file.</summary>
internal sealed class AssessmentSelection
{
    private readonly FamilyAssessment assessment;
    private readonly bool touched;
    private readonly ILookup<string, AssessmentPart> partsByType;
    private readonly ILookup<string, AssessmentPart> partsByFile;
    private readonly ILookup<string, Finding> findingsByType;
    private readonly Dictionary<string, FindingOrigin[]> origins;

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

        var located = scope is null ? [] : new ChangedFindings(scope).Select(assessment).DistinctBy(entry => entry.Finding.Id).ToArray();
        origins = located.ToDictionary(entry => entry.Finding.Id, entry => entry.Origins, StringComparer.Ordinal);
        Findings = (touched ? located.Select(entry => entry.Finding) : assessment.Findings)
            .DistinctBy(finding => finding.Id).OrderBy(finding => finding.Id, StringComparer.Ordinal).ToArray();
        findingsByType = Findings.SelectMany(finding => finding.EntityIds.Select(id => (Id: id, Finding: finding)))
            .ToLookup(entry => entry.Id, entry => entry.Finding, StringComparer.Ordinal);
    }

    internal TypeFact[] Types { get; }
    internal Finding[] Findings { get; }

    internal IEnumerable<string> ScoringFiles(Finding finding) => touched
        ? origins[finding.Id].Select(origin => origin.Location.File)
        : finding.Locations.Select(location => location.File);

    internal bool ScoresFile(Finding finding, string file, HashSet<string> typeIds) => touched
        ? origins[finding.Id].Any(origin => origin.Location.File == file && typeIds.Contains(origin.TypeId))
        : finding.EntityIds.Any(typeIds.Contains);

    internal EntityScoringEvidence For(EntityAssessment entity)
    {
        var byFile = entity.Kind == "file";
        var parts = byFile && touched ? partsByFile[entity.File] : entity.TypeIds.SelectMany(id => partsByType[id]);
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
        var findings = findingsByType[typeId];
        if (touched && entity.Kind == "file")
            findings = findings.Where(finding => origins[finding.Id].Any(origin =>
                origin.TypeId == typeId && origin.Location.File == entity.File));
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
