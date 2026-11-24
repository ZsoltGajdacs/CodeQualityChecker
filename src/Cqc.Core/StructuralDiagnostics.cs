namespace Cqc.Core;

internal static class StructuralDiagnostics
{
    internal static IEnumerable<Diagnostic> For(AnalysisFacts facts, ProjectSnapshot snapshot) =>
        snapshot.SourceOnly ? SourceOnly(facts, snapshot) : EvidenceAware(facts);

    private static IEnumerable<Diagnostic> SourceOnly(AnalysisFacts facts, ProjectSnapshot snapshot)
    {
        var languages = snapshot.SelectedFiles.Select(file => snapshot.SourceLanguages.GetValueOrDefault(file)).ToHashSet(StringComparer.Ordinal);
        return facts.Diagnostics.Where(StructuralEvidence.IsAnalyzerFailure).Concat(facts.SyntaxGaps.Select(Describe))
            .Where(diagnostic => diagnostic.Language is null || languages.Contains(diagnostic.Language)).Distinct();
    }

    private static IEnumerable<Diagnostic> EvidenceAware(AnalysisFacts facts)
    {
        var gaps = facts.SyntaxGaps.ToLookup(GapKey);
        var reported = facts.Diagnostics.Select(Key).ToHashSet();
        return facts.Diagnostics.Select(diagnostic => WithDetails(diagnostic, gaps[Key(diagnostic)].FirstOrDefault()))
            .Concat(facts.SyntaxGaps.Where(gap => !reported.Contains(GapKey(gap))).Select(Describe)).Distinct();
    }

    private static Diagnostic WithDetails(Diagnostic diagnostic, SyntaxGap? gap) => gap is null ? diagnostic
        : diagnostic with { Location = diagnostic.Location ?? gap.Location, Construct = diagnostic.Construct ?? gap.Construct };

    private static Diagnostic Describe(SyntaxGap gap) => new(gap.Code, "Source structure could not be fully analyzed.",
        gap.File, gap.Language) { Location = gap.Location, Construct = gap.Construct };

    private static (string Code, string? File) Key(Diagnostic diagnostic) => (diagnostic.Code, diagnostic.File);

    private static (string Code, string? File) GapKey(SyntaxGap gap) =>
        (gap.DiagnosticCode ?? gap.Code, gap.File);
}
