namespace Cqc.Core;

internal static class StructuralDiagnostics
{
    internal static IEnumerable<Diagnostic> For(AnalysisFacts facts, ProjectSnapshot snapshot)
    {
        var reported = facts.Diagnostics.ToLookup(Key);
        var gaps = facts.SyntaxGaps.SelectMany(gap => reported[GapKey(gap)].DefaultIfEmpty()
            .Select(diagnostic => Describe(gap, diagnostic, snapshot.SourceOnly)));
        if (!snapshot.SourceOnly)
        {
            var projected = facts.SyntaxGaps.Select(GapKey).ToHashSet();
            return facts.Diagnostics.Where(diagnostic => !projected.Contains(Key(diagnostic))).Concat(gaps).Distinct();
        }
        var languages = snapshot.SelectedFiles.Select(file => snapshot.SourceLanguages.GetValueOrDefault(file)).ToHashSet(StringComparer.Ordinal);
        return facts.Diagnostics.Where(StructuralEvidence.IsAnalyzerFailure).Concat(gaps)
            .Where(diagnostic => diagnostic.Language is null || languages.Contains(diagnostic.Language)).Distinct();
    }

    private static Diagnostic Describe(SyntaxGap gap, Diagnostic? reported, bool sourceOnly)
    {
        var diagnostic = reported ?? new(gap.Code, "Source structure could not be fully analyzed.", gap.File, gap.Language);
        return diagnostic with
        {
            Code = sourceOnly ? gap.Code : diagnostic.Code,
            Message = gap.Message ?? diagnostic.Message,
            Location = diagnostic.Location ?? gap.Location,
            Construct = diagnostic.Construct ?? gap.Construct
        };
    }

    private static (string Code, string? File, string? Language) Key(Diagnostic diagnostic) =>
        (diagnostic.Code, diagnostic.File, diagnostic.Language);

    private static (string Code, string? File, string? Language) GapKey(SyntaxGap gap) =>
        (gap.DiagnosticCode ?? gap.Code, gap.File, gap.Language);
}
