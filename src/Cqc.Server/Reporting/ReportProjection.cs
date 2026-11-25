using Cqc.Core;

namespace Cqc.Server.Reporting;

internal static class ReportProjection
{
    public static ReportSummary Summary(object report, string id, DateTimeOffset expiry, string scope = "repository") => report switch
    {
        AnalysisResponse analysis => new()
        {
            ReportId = id, ExpiresAt = expiry, SourceFingerprint = analysis.SourceFingerprint, HotspotHistory = analysis.HotspotHistory,
            ConfigurationFingerprint = analysis.ConfigurationFingerprint, Scope = analysis.Scope,
            FileCount = analysis.AnalyzedFiles.Length, ModuleCount = analysis.Modules.Length,
            Families = analysis.Results.Select(family => Family(family, analysis.SourceOnly)).ToArray(),
            Diagnostics = Counts(ReportDiagnostics.For(analysis).Select(row => row.Diagnostic)),
            AnalysisNote = analysis.SourceOnly && analysis.SupportsProjectExecution
                ? "Running with `execute: true` may reveal additional findings." : null,
            RemainingDiagnosticGroups = Remaining(ReportDiagnostics.For(analysis).Select(row => row.Diagnostic)), Kind = "analysis"
        },
        InspectionResponse inspection => new()
        {
            ReportId = id, ExpiresAt = expiry, SourceFingerprint = inspection.Project.SourceFingerprint, Scope = scope,
            FileCount = inspection.Project.SelectedFiles.Length, ModuleCount = inspection.Project.Modules.Length, Families = [],
            Diagnostics = Counts(inspection.Project.Diagnostics.Concat(inspection.Prerequisites)),
            RemainingDiagnosticGroups = Remaining(inspection.Project.Diagnostics.Concat(inspection.Prerequisites)), Kind = "inspection"
        },
        _ => throw new ArgumentException("Unsupported report type.")
    };
    private static FamilySummary Family(FamilyResult family, bool sourceOnly)
    {
        var staticOnly = sourceOnly && family.Family == "code";
        return new(family.Family, family.Status, family.Entities.Length, family.Findings.Length)
        {
            Scores = staticOnly ? null : ScoreDistribution(family.Entities.Select(e => e.Score)),
            EntityStatusCounts = StatusCounts(family.Entities, staticOnly),
            StaticComplexityScores = ScoreDistribution(family.Entities.Where(e => e.StaticComplexity is not null)
                .Select(e => e.StaticComplexity!.Score)),
            ScoredFileCount = family.ScoredFileCount,
            UnscoredFileCount = family.UnscoredFileCount
        };
    }

    private static EntityStatusCounts StatusCounts(EntityScore[] entities, bool staticOnly)
    {
        var scored = 0;
        var notApplicable = 0;
        foreach (var entity in entities)
        {
            var score = staticOnly ? entity.StaticComplexity?.Score : entity.Score;
            var status = staticOnly ? entity.StaticComplexity?.Status ?? entity.Status : entity.Status;
            if (score is not null) scored++;
            else if (status == "notApplicable") notApplicable++;
        }
        return new(scored, entities.Length - scored - notApplicable, notApplicable);
    }
    private static ScoreCount[] ScoreDistribution(IEnumerable<double?> scores) =>
        scores.GroupBy(score => score).OrderBy(g => g.Key).Select(g => new ScoreCount(g.Key, g.Count())).ToArray();
    private static DiagnosticCount[] Counts(IEnumerable<Diagnostic> diagnostics) => diagnostics.GroupBy(d => d.Code)
        .OrderByDescending(group => group.Any(diagnostic => diagnostic.Scope == "selected"))
        .ThenBy(group => group.Key, StringComparer.Ordinal).Take(20).Select(group => new DiagnosticCount(group.Key, group.Count())
        {
            SelectedCount = CountScope(group, "selected"),
            ContextCount = CountScope(group, "context"),
            ProjectWideCount = CountScope(group, "project"),
            Extensions = Extensions(group)
        }).ToArray();
    private static string[]? Extensions(IEnumerable<Diagnostic> diagnostics)
    {
        var extensions = diagnostics.Select(diagnostic => diagnostic.Extension).OfType<string>()
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return extensions.Length == 0 ? null : extensions;
    }
    private static int? CountScope(IEnumerable<Diagnostic> diagnostics, string scope)
    {
        var count = diagnostics.Count(diagnostic => diagnostic.Scope == scope);
        return count > 0 ? count : null;
    }
    private static int Remaining(IEnumerable<Diagnostic> diagnostics) => Math.Max(0, diagnostics.Select(d => d.Code).Distinct().Count() - 20);
}
