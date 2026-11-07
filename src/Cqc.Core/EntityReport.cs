namespace Cqc.Core;

/// <summary>Rolls method/component findings into deduplicated class and selected-file reports.</summary>
internal sealed class EntityReport
{
    private readonly IGrouping<string, TypeFact>[] types;
    private readonly Finding[] findings;
    private readonly Func<string, bool> complete;
    private readonly Func<string, SortedDictionary<string, double?>> metrics;
    internal sealed record Result(EntityScore[] Entities, Finding[] Findings);

    internal EntityReport(FamilyAssessment assessment)
    {
        types = assessment.Types.GroupBy(t => t.Id, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
        findings = assessment.Findings.DistinctBy(f => f.Id).OrderBy(f => f.Id, StringComparer.Ordinal).ToArray();
        complete = assessment.Complete;
        metrics = assessment.Metrics;
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

    private EntityScore ClassScore(IGrouping<string, TypeFact> type)
    {
        var file = type.Select(t => t.Location.File).Order(StringComparer.Ordinal).First();
        var applicable = findings.Where(f => f.EntityIds.Contains(type.Key, StringComparer.Ordinal)).ToArray();
        return Score(new() { Id = type.Key, Kind = "class", File = file, Findings = applicable, Complete = Complete(type.Key), Metrics = metrics(type.Key) });
    }

    private EntityScore FileScore(string file)
    {
        var ids = types.Where(g => g.Any(t => t.Location.File == file)).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        var applicable = findings.Where(f => f.Locations.Any(l => l.File == file) && f.EntityIds.Any(ids.Contains)).ToArray();
        return Score(new() { Id = "file:" + file, Kind = "file", File = file, Findings = applicable, Complete = ids.All(Complete), Metrics = FileMetrics(ids) });
    }

    private SortedDictionary<string, double?> FileMetrics(IEnumerable<string> ids)
    {
        var result = RuleContext.Metrics();
        foreach (var id in ids)
            foreach (var metric in metrics(id)) result[$"class:{id}:{metric.Key}"] = metric.Value;
        return result;
    }

    private bool Complete(string id) => complete(id) && types.Single(g => g.Key == id).All(t => t.Complete);

    private static EntityScore Score(EntityAssessment assessment) => new(assessment.Id, assessment.Kind, assessment.File,
        assessment.Complete ? "complete" : "incomplete",
        assessment.Complete ? Math.Max(0, 10 - assessment.Findings.Sum(f => f.Deduction)) : null,
        assessment.Findings.Select(f => f.Id).Order(StringComparer.Ordinal).ToArray(), assessment.Metrics);
}
