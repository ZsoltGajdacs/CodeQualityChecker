namespace Cqc.Core;

public sealed record EvaluationRequest
{
    public required ProjectSnapshot Snapshot { get; init; }
    public required AnalysisFacts Facts { get; init; }
    public QualityConfig Config { get; init; } = new();
    public CoverageEvidence? Evidence { get; init; }
    public required string ConfigurationFingerprint { get; init; }
    public required string[] Families { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>Pure, deterministic quality rules shared by every language adapter.</summary>
public static class QualityEngine
{
    public static FamilyResult[] Evaluate(EvaluationRequest request)
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        var context = new RuleContext(request.Snapshot, request.Facts, request.CancellationToken);
        if (request.Snapshot.SourceOnly) return StructuralReview.Evaluate(context, request.Config);
        return request.Families.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(family => family switch
        {
            "code" => ProductionRules.Evaluate(context, request.Config, request.Evidence, request.ConfigurationFingerprint),
            "tests" => TestRules.Evaluate(context, request.Config),
            "duplication" => DuplicationRules.Evaluate(context, request.Config),
            "dependencies" => DependencyRules.Evaluate(context, request.Config),
            _ => throw new ArgumentException($"Unknown check family: {family}", nameof(request))
        }).ToArray();
    }
}
