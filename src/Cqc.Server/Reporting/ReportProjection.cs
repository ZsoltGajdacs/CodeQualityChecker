using Cqc.Core;

namespace Cqc.Server.Reporting;

internal static class ReportProjection
{
    public static ReportSummary Summary(object report, string id, DateTimeOffset expiry, string scope = "repository") => report switch
    {
        AnalysisResponse analysis => new()
        {
            ReportId = id, ExpiresAt = expiry, SourceFingerprint = analysis.SourceFingerprint,
            ConfigurationFingerprint = analysis.ConfigurationFingerprint, Scope = analysis.Scope,
            FileCount = analysis.AnalyzedFiles.Length, ModuleCount = analysis.Modules.Length,
            Families = analysis.Results.Select(Family).ToArray(), Diagnostics = Counts(AnalysisDiagnostics(analysis)),
            RemainingDiagnosticGroups = Remaining(AnalysisDiagnostics(analysis)), Kind = "analysis"
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
    private static IEnumerable<Diagnostic> AnalysisDiagnostics(AnalysisResponse report) => report.Diagnostics.Concat(report.Results.SelectMany(f => f.Diagnostics));
    private static FamilySummary Family(FamilyResult family) => new(family.Family, family.Status, family.Entities.Length, family.Findings.Length,
        family.Entities.GroupBy(e => e.Score).OrderBy(g => g.Key).Select(g => new ScoreCount(g.Key, g.Count())).ToArray())
        { StaticComplexityScores = family.Entities.Where(e => e.StaticComplexity is not null)
            .GroupBy(e => e.StaticComplexity!.Score).OrderBy(g => g.Key).Select(g => new ScoreCount(g.Key, g.Count())).ToArray() };
    private static DiagnosticCount[] Counts(IEnumerable<Diagnostic> diagnostics) => diagnostics.GroupBy(d => d.Code)
        .OrderBy(g => g.Key, StringComparer.Ordinal).Take(20).Select(g => new DiagnosticCount(g.Key, g.Count())).ToArray();
    private static int Remaining(IEnumerable<Diagnostic> diagnostics) => Math.Max(0, diagnostics.Select(d => d.Code).Distinct().Count() - 20);
}
