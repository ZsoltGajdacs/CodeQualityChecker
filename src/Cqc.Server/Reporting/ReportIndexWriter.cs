using System.Text.Json;
using Cqc.Core;

namespace Cqc.Server.Reporting;

internal sealed class ReportIndexWriter(Stream stream, JsonSerializerOptions options, CancellationToken token)
{
    private readonly List<IndexedRow> rows = [];
    public async Task<IndexedRow[]> WriteAsync(object report)
    {
        if (report is AnalysisResponse analysis) await Analysis(analysis);
        else if (report is InspectionResponse inspection) await Inspection(inspection);
        return rows.ToArray();
    }
    private async Task Analysis(AnalysisResponse analysis)
    {
        foreach (var file in analysis.AnalyzedFiles) await Add("files", null, [], file);
        foreach (var module in analysis.Modules) await Add("modules", null, [], module);
        foreach (var row in ReportDiagnostics.For(analysis).OrderByDescending(row => row.Diagnostic.Scope == "selected"))
            await AddDiagnostic(row);
        foreach (var family in analysis.Results) await Family(family);
    }
    private async Task Inspection(InspectionResponse inspection)
    {
        foreach (var file in inspection.Project.SelectedFiles) await Add("files", null, [], file);
        foreach (var module in inspection.Project.Modules) await Add("modules", null, [], module);
        foreach (var diagnostic in inspection.Project.Diagnostics.OrderByDescending(diagnostic => diagnostic.Scope == "selected"))
            await Add("diagnostics", null, [], diagnostic);
        foreach (var prerequisite in inspection.Prerequisites) await Add("prerequisites", null, [], prerequisite);
    }
    private async Task Family(FamilyResult family)
    {
        var findingEntities = family.Entities.SelectMany(entity => FindingIds(entity).Select(id => (id, entity.EntityId)))
            .ToLookup(entry => entry.id, entry => entry.EntityId, StringComparer.Ordinal);
        foreach (var finding in family.Findings) await Add("findings", family.Family, finding.EntityIds.Concat(findingEntities[finding.Id]).Distinct(StringComparer.Ordinal).ToArray(), new { family = family.Family, finding.Id, finding.Rule, finding.Message, finding.Advice, finding.EntityIds, finding.Locations, finding.Deduction });
        foreach (var entity in family.Entities)
        {
            await Add("entities", family.Family, [entity.EntityId], new EntityPageItem(family.Family, entity.EntityId,
                entity.Kind, entity.File, entity.Status, entity.Score, entity.Blockers, entity.StaticComplexity,
                FindingIds(entity).Count(), entity.Metrics.Count));
            foreach (var metric in entity.Metrics)
                await Add("metrics", family.Family, [entity.EntityId], new { family = family.Family, entity.EntityId, name = metric.Key, value = metric.Value });
        }
    }

    private static IEnumerable<string> FindingIds(EntityScore entity) =>
        entity.FindingIds.Union(entity.StaticComplexity?.FindingIds ?? [], StringComparer.Ordinal);

    private Task AddDiagnostic(ReportDiagnostics.Row row)
    {
        var diagnostic = row.Diagnostic;
        if (row.Family is null) return Add("diagnostics", null, [], diagnostic);
        return Add("diagnostics", row.Family, [], new
        {
            family = row.Family, diagnostic.Code, diagnostic.Message, diagnostic.File, diagnostic.Language,
            diagnostic.Scope, diagnostic.Location, diagnostic.Construct, diagnostic.Extension
        });
    }
    private async Task Add(string section, string? family, string[] entities, object value)
    {
        token.ThrowIfCancellationRequested();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, options);
        var offset = stream.Position;
        await stream.WriteAsync(bytes, token);
        rows.Add(new(section, family, entities, offset, bytes.Length));
    }
}

internal sealed record EntityPageItem(string Family, string EntityId, string Kind, string File, string Status,
    double? Score, string[] Blockers, AssessmentResult? StaticComplexity, int FindingCount, int MetricCount);
