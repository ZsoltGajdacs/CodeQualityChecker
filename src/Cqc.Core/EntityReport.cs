namespace Cqc.Core;

/// <summary>Projects family assessments into selected classes/files; it owns no evidence policy.</summary>
internal sealed class EntityReport
{
    private readonly IGrouping<string, TypeFact>[] types;
    private readonly Finding[] findings;
    private readonly FamilyAssessment assessment;
    private readonly ILookup<string, string> typesByFile;
    private readonly ILookup<string, Finding> findingsByEntity;
    private readonly ILookup<string, Finding> findingsByFile;
    internal sealed record Result(EntityScore[] Entities, Finding[] Findings);

    internal EntityReport(FamilyAssessment assessment)
    {
        this.assessment = assessment;
        types = assessment.Types.GroupBy(t => t.Id, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
        findings = assessment.Findings.DistinctBy(f => f.Id).OrderBy(f => f.Id, StringComparer.Ordinal).ToArray();
        typesByFile = types.SelectMany(g => g.Select(t => t.Location.File).Distinct(StringComparer.Ordinal)
            .Select(file => (File: file, Id: g.Key))).ToLookup(p => p.File, p => p.Id, StringComparer.Ordinal);
        findingsByEntity = findings.SelectMany(f => f.EntityIds.Distinct(StringComparer.Ordinal)
            .Select(id => (Id: id, Finding: f))).ToLookup(p => p.Id, p => p.Finding, StringComparer.Ordinal);
        findingsByFile = findings.SelectMany(f => f.Locations.Select(l => l.File).Distinct(StringComparer.Ordinal)
            .Select(file => (File: file, Finding: f))).ToLookup(p => p.File, p => p.Finding, StringComparer.Ordinal);
    }

    internal Result Build(string[] selectedFiles)
    {
        var selected = selectedFiles.ToHashSet(StringComparer.Ordinal);
        var visible = types.Where(g => g.Any(t => selected.Contains(t.Location.File))).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        var classes = types.Where(g => visible.Contains(g.Key)).Select(ClassScore);
        var files = types.SelectMany(t => t).Select(t => t.Location.File).Distinct(StringComparer.Ordinal)
            .Where(selected.Contains).Order(StringComparer.Ordinal).Select(FileScore);
        var entities = classes.Concat(files).OrderBy(e => e.File, StringComparer.Ordinal)
            .ThenBy(e => e.Kind, StringComparer.Ordinal).ThenBy(e => e.EntityId, StringComparer.Ordinal).ToArray();
        var visibleFindings = findings.Where(f => f.EntityIds.Any(visible.Contains))
            .OrderBy(f => f.Locations.FirstOrDefault()?.File, StringComparer.Ordinal)
            .ThenBy(f => f.Locations.FirstOrDefault()?.StartLine).ThenBy(f => f.Id, StringComparer.Ordinal).ToArray();
        return new(entities, visibleFindings);
    }

    private EntityScore ClassScore(IGrouping<string, TypeFact> type) => Score(new()
    {
        Id = type.Key, Kind = "class", File = type.Select(t => t.Location.File).Order(StringComparer.Ordinal).First(),
        TypeIds = [type.Key], Findings = findingsByEntity[type.Key].ToArray(),
        Metrics = assessment.Metrics(type.Key)
    });

    private EntityScore FileScore(string file)
    {
        var ids = typesByFile[file].ToHashSet(StringComparer.Ordinal);
        var applicable = findingsByFile[file].Where(f => f.EntityIds.Any(ids.Contains)).ToArray();
        return Score(new() { Id = "file:" + file, Kind = "file", File = file, TypeIds = ids.ToArray(), Findings = applicable, Metrics = FileMetrics(ids) });
    }

    private SortedDictionary<string, double?> FileMetrics(IEnumerable<string> ids)
    {
        var result = RuleContext.Metrics();
        foreach (var id in ids)
            foreach (var metric in assessment.Metrics(id)) result[$"class:{id}:{metric.Key}"] = metric.Value;
        return result;
    }

    private EntityScore Score(EntityAssessment entity)
    {
        var keys = entity.TypeIds;
        var primary = entity.Findings.Where(finding => !assessment.StaticOnlyFindingIds.Contains(finding.Id));
        var applicable = keys.Where(assessment.PrimaryScoreApplies).ToArray();
        var result = applicable.Length == 0 ? new("notApplicable", null, [], [])
            : AssessmentEvidence.Score(primary, applicable.SelectMany(assessment.Blockers));
        var secondary = assessment.StaticBlockers is null ? null : AssessmentEvidence.Score(
            entity.Findings.Where(f => f.Rule == "code.complexity"), keys.SelectMany(assessment.StaticBlockers));
        return new(entity.Id, entity.Kind, entity.File, result.Status, result.Score, result.FindingIds, entity.Metrics)
        { Blockers = result.Blockers, StaticComplexity = secondary };
    }
}
