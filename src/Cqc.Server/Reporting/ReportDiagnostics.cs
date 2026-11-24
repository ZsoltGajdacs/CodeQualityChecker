using Cqc.Core;

namespace Cqc.Server.Reporting;

internal static class ReportDiagnostics
{
    internal readonly record struct Row(string? Family, Diagnostic Diagnostic);

    internal static IEnumerable<Row> For(AnalysisResponse report) =>
        report.Diagnostics.Select(diagnostic => new Row(null, diagnostic))
            .Concat(report.Results.SelectMany(family => family.Diagnostics.Select(diagnostic => new Row(family.Family, diagnostic))));
}
