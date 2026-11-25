namespace Cqc.Core;

internal enum EvidenceMode { SourceOnly, Cached, Execute }

internal sealed class AnalysisPolicy
{
    private static readonly string[] StructuralFamilies = ["code", "duplication", "hotspots"];
    private static readonly string[] ReviewFamilies = [.. StructuralFamilies, "tests", "dependencies"];

    private AnalysisPolicy(string[] families, EvidenceMode evidence, bool reviewTouchedCode)
    {
        Families = families;
        Evidence = evidence;
        ReviewTouchedCode = reviewTouchedCode;
    }

    internal string[] Families { get; }
    internal EvidenceMode Evidence { get; }
    internal bool ReviewTouchedCode { get; }
    internal bool SourceOnly => Evidence == EvidenceMode.SourceOnly;
    internal bool Executes => Evidence == EvidenceMode.Execute;
    internal bool IncludesHotspots => Families.Contains("hotspots", StringComparer.Ordinal);

    internal static AnalysisPolicy ForChecks(AnalysisRequest request, string[] families) =>
        new(families, request.Execute ? EvidenceMode.Execute : EvidenceMode.Cached, false);

    internal static AnalysisPolicy ForAll(AnalysisRequest request) => ForChecks(request, ReviewFamilies);

    internal static AnalysisPolicy ForHotspots() => new(["hotspots"], EvidenceMode.SourceOnly, false);

    internal static AnalysisPolicy ForReview(AnalysisRequest request) =>
        request.Execute
            ? new(ReviewFamilies, EvidenceMode.Execute, request.Scope == "changed")
            : new(StructuralFamilies, EvidenceMode.SourceOnly, request.Scope == "changed");
}
