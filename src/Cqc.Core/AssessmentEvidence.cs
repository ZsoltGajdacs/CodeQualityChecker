namespace Cqc.Core;

/// <summary>Independent facts supplied by adapters; scoring requirements belong to family policies.</summary>
public sealed record EvidenceState(bool Syntax, bool Identity, bool Calls, bool Dependencies,
    bool VerifiedContext, string[] Blockers);
public sealed record AnalysisContext(string Id, string ModulePath, string SourceSet,
    string[] ReachableContexts, bool Verified, string[] Blockers);
public sealed record SyntaxGap(string File, string Language, string? ContextId, string Code)
{
    public bool UnknownEntities { get; init; } = true;
    public Location? Location { get; init; }
    public string? Construct { get; init; }
    public string? TypeId { get; init; }
}
public sealed record AssessmentResult(string Status, double? Score, string[] FindingIds, string[] Blockers);

internal static class AssessmentEvidence
{
    private static readonly EvidenceState Missing = new(false, false, false, false, false, ["evidence.missing"]);
    internal static EvidenceState Of(MethodFact method) => method.Evidence ?? Missing;
    internal static EvidenceState Of(TypeFact type) => type.Evidence ?? Missing;

    internal static string[] Syntax(TypeFact type, IEnumerable<MethodFact> methods) => Reasons(
        (Of(type).Syntax && methods.All(m => Of(m).Syntax), "syntax.incomplete"));

    internal static string[] Production(MethodFact method, bool coverage) => Reasons(
        (Of(method).Syntax, "syntax.incomplete"), (Of(method).Identity, "identity.unresolved"),
        (Of(method).VerifiedContext, "context.unverified"), (coverage, "coverage.missing"));

    internal static string[] Test(MethodFact method) => Reasons(
        (Of(method).Syntax, "syntax.incomplete"), (Of(method).Identity, "identity.unresolved"),
        (Of(method).Calls, "tests.unresolvedCalls"), (Of(method).VerifiedContext, "context.unverified"));

    internal static string[] Reasons(params (bool Satisfied, string Code)[] requirements) => requirements
        .Where(r => !r.Satisfied).Select(r => r.Code).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    internal static AssessmentResult Score(IEnumerable<Finding> findings, IEnumerable<string> blockers)
    {
        var distinct = findings.DistinctBy(f => f.Id).OrderBy(f => f.Id, StringComparer.Ordinal).ToArray();
        var missing = blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new(missing.Length == 0 ? "complete" : "incomplete",
            missing.Length == 0 ? Math.Max(0, 10 - distinct.Sum(f => f.Deduction)) : null,
            distinct.Select(f => f.Id).ToArray(), missing);
    }
}
