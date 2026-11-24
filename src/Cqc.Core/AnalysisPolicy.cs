namespace Cqc.Core;

internal enum EvidenceMode { SourceOnly, Cached, Execute }

internal sealed class AnalysisPolicy
{
    private static readonly string[] StructuralFamilies = ["code", "duplication"];
    private static readonly string[] ReviewFamilies = ["code", "tests", "duplication", "dependencies"];

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

    internal static AnalysisPolicy ForChecks(AnalysisRequest request, string[] families) =>
        new(families, request.Execute ? EvidenceMode.Execute : EvidenceMode.Cached, false);

    internal static AnalysisPolicy ForReview(AnalysisRequest request) =>
        request.Execute
            ? new(ReviewFamilies, EvidenceMode.Execute, request.Scope == "changed")
            : new(StructuralFamilies, EvidenceMode.SourceOnly, request.Scope == "changed");
}
