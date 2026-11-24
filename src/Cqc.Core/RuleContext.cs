namespace Cqc.Core;

internal sealed class RuleContext(ProjectSnapshot snapshot, AnalysisFacts facts, CancellationToken cancellationToken = default)
{
    public CancellationToken CancellationToken { get; } = cancellationToken;
    public ProjectSnapshot Snapshot { get; } = snapshot;
    public AnalysisFacts Facts { get; } = facts;
    public FactIndex Index { get; } = new(facts);
    internal ChangedCodeScope? ChangedCode { get; init; }
    private ChangedFindings? changedDependencies;
    internal ChangedFindings? ChangedDependencies => ChangedCode is null ? null : changedDependencies ??= new(ChangedCode, Index);
    private StructuralEvidence? structure;
    public StructuralEvidence Structure => structure ??= new(Snapshot, Facts);
    public MethodFact[] Methods => Index.Methods;

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
        var changed = ProjectChanges(assessment);
        var report = new EntityReport(assessment, changed).Build(Snapshot.SelectedFiles);
        if (Snapshot.SourceOnly)
            return new(assessment.Family, Status(report.Entities, assessment.Incomplete)) { Entities = report.Entities, Findings = report.Findings };
        var failures = Facts.Diagnostics.Where(d => d.Code == "analyzer_failed").ToArray();
        var orderedDiagnostics = assessment.Diagnostics.Concat(failures).Distinct()
            .OrderBy(d => d.File, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal)
            .ThenBy(d => d.Message, StringComparer.Ordinal).ToArray();
        var selected = Snapshot.SelectedFiles.ToHashSet(StringComparer.Ordinal);
        var applicable = (changed?.Types ?? assessment.Types).Select(type => type.Location.File).ToHashSet(StringComparer.Ordinal);
        var missingSyntax = Facts.SyntaxGaps.Any(g => selected.Contains(g.File) && (g.UnknownEntities || applicable.Contains(g.File)));
        return new(assessment.Family, Status(report.Entities, assessment.Incomplete || failures.Length != 0 || missingSyntax))
        {
            Entities = report.Entities, Findings = report.Findings, Diagnostics = orderedDiagnostics
        };
    }

    private ChangedAssessment? ProjectChanges(FamilyAssessment assessment)
    {
        if (ChangedCode is not { } scope) return null;
        var findings = assessment.Family == "dependencies" ? ChangedDependencies! : new ChangedFindings(scope);
        return new(assessment, scope, findings);
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
