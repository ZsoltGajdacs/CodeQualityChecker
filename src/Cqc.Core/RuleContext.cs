namespace Cqc.Core;

internal sealed class RuleContext(ProjectSnapshot snapshot, AnalysisFacts facts, CancellationToken cancellationToken = default)
{
    public CancellationToken CancellationToken { get; } = cancellationToken;
    public ProjectSnapshot Snapshot { get; } = snapshot;
    public AnalysisFacts Facts { get; } = facts;
    public MethodFact[] Methods { get; } = facts.Methods.OrderBy(m => m.Id, StringComparer.Ordinal)
        .ThenBy(m => m.Location.File, StringComparer.Ordinal).ToArray();

    public static SortedDictionary<string, double?> Metrics() => new(StringComparer.Ordinal);

    public static Finding Finding(FindingDefinition definition)
    {
        var ids = definition.Entities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var ordered = definition.Locations.Distinct().OrderBy(l => l.File, StringComparer.Ordinal)
            .ThenBy(l => l.StartLine).ThenBy(l => l.EndLine).ToArray();
        return new(Wire.Hash(definition.Rule + "|" + definition.Key), definition.Rule, definition.Message, definition.Advice, ids, ordered);
    }

    public FamilyResult Finish(FamilyAssessment assessment)
    {
        var report = new EntityReport(assessment).Build(Snapshot.SelectedFiles);
        var failures = Facts.Diagnostics.Where(d => d.Code == "analyzer_failed").ToArray();
        var orderedDiagnostics = assessment.Diagnostics.Concat(failures).Distinct()
            .OrderBy(d => d.File, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal)
            .ThenBy(d => d.Message, StringComparer.Ordinal).ToArray();
        var selected = Snapshot.SelectedFiles.ToHashSet(StringComparer.Ordinal);
        var applicable = assessment.Types.Select(type => type.Location.File).ToHashSet(StringComparer.Ordinal);
        var missingSyntax = Facts.SyntaxGaps.Any(g => selected.Contains(g.File) && (g.UnknownEntities || applicable.Contains(g.File)));
        return new(assessment.Family, Status(report.Entities, failures.Length != 0 || missingSyntax), report.Entities, report.Findings, orderedDiagnostics);
    }

    private static string Status(EntityScore[] entities, bool failed)
    {
        if (failed || entities.Any(e => e.Status == "incomplete")) return "incomplete";
        return entities.Length == 0 ? "notApplicable" : "complete";
    }

    public IEnumerable<TypeFact> TypesFor(IEnumerable<MethodFact> methods)
    {
        var ids = methods.Select(m => m.TypeId).ToHashSet(StringComparer.Ordinal);
        return Facts.Types.Where(t => ids.Contains(t.Id));
    }

}
