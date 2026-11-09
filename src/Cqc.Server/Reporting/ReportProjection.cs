using Cqc.Core;

namespace Cqc.Server.Reporting;

internal static class ReportProjection
{
    public static ReportSummary Summary(object report, string id, DateTimeOffset expiry, string scope = "repository") => report switch
    {
        AnalysisResponse analysis => new(id, expiry, analysis.SourceFingerprint, analysis.ConfigurationFingerprint,
            analysis.Scope, analysis.AnalyzedFiles.Length, analysis.Modules.Length,
            analysis.Results.Select(Family).ToArray(),
            Counts(AnalysisDiagnostics(analysis)), Remaining(AnalysisDiagnostics(analysis)), "analysis"),
        InspectionResponse inspection => new(id, expiry, inspection.Project.SourceFingerprint, null, scope,
            inspection.Project.SelectedFiles.Length, inspection.Project.Modules.Length, [],
            Counts(inspection.Project.Diagnostics.Concat(inspection.Prerequisites)),
            Remaining(inspection.Project.Diagnostics.Concat(inspection.Prerequisites)), "inspection"),
        _ => throw new ArgumentException("Unsupported report type.")
    };
    private static IEnumerable<Diagnostic> AnalysisDiagnostics(AnalysisResponse report) => report.Diagnostics.Concat(report.Results.SelectMany(f => f.Diagnostics));
    private static FamilySummary Family(FamilyResult family) => new(family.Family, family.Status, family.Entities.Length, family.Findings.Length,
        family.Entities.GroupBy(e => e.Score).OrderBy(g => g.Key).Select(g => new ScoreCount(g.Key, g.Count())).ToArray());
    private static DiagnosticCount[] Counts(IEnumerable<Diagnostic> diagnostics) => diagnostics.GroupBy(d => d.Code)
        .OrderBy(g => g.Key, StringComparer.Ordinal).Take(20).Select(g => new DiagnosticCount(g.Key, g.Count())).ToArray();
    private static int Remaining(IEnumerable<Diagnostic> diagnostics) => Math.Max(0, diagnostics.Select(d => d.Code).Distinct().Count() - 20);
}
