namespace Cqc.Core;

internal static class ProductionRules
{
    private sealed record Assessment(MethodFact Method, double? Coverage, double? Crap, Finding[] Findings)
    {
        public MethodCoverage? Report { get; init; }
        public string[] Blockers => AssessmentEvidence.Production(Method, Coverage is not null);
        public bool Complete => Blockers.Length == 0;
    }

    public static FamilyResult Evaluate(RuleContext context, QualityConfig config,
        CoverageEvidence? evidence, string configFingerprint)
    {
        var testTypes = context.Facts.Types.Where(t => t.IsTest).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var methods = context.Methods.Where(m => !m.IsTest && !m.IsFixture && !testTypes.Contains(m.TypeId)).ToArray();
        var coverage = MatchingCoverage(context.Snapshot.SourceFingerprint, configFingerprint, evidence);
        var assessments = methods.Select(m =>
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return Assess(m, config, coverage.GetValueOrDefault(m.Id));
        }).ToArray();
        var missing = assessments.Where(a => !a.Complete).Select(a => a.Method.TypeId).ToHashSet(StringComparer.Ordinal);
        var metrics = assessments.GroupBy(a => a.Method.TypeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, Metrics, StringComparer.Ordinal);
        var diagnostics = missing.Count == 0 ? Array.Empty<Diagnostic>() :
            [new Diagnostic("coverage.incomplete", "Combined scoring requires complete syntax, resolved method identities and source/configuration-matched coverage. Inspect entity blockers; execute=true can collect coverage but may not resolve every blocker.")];
        return context.Finish(new() { Family = "code", Types = context.TypesFor(methods)
                .Concat(context.Facts.Types.Where(type => !type.IsTest && !AssessmentEvidence.Of(type).Syntax)).Distinct(), Findings = assessments.SelectMany(a => a.Findings),
            Blockers = id => assessments.Where(a => a.Method.TypeId == id).SelectMany(a => a.Blockers)
                .Concat(context.Facts.Types.Where(t => t.Id == id).SelectMany(t => AssessmentEvidence.Syntax(t, []))).Distinct().ToArray(),
            StaticBlockers = id => context.Facts.Types.Where(t => t.Id == id)
                .SelectMany(t => AssessmentEvidence.Syntax(t, methods.Where(m => m.TypeId == id))).Distinct().ToArray(), Metrics = id => metrics.GetValueOrDefault(id) ?? RuleContext.Metrics(), Diagnostics = diagnostics });
    }

    private static Dictionary<string, MethodCoverage> MatchingCoverage(string source, string config, CoverageEvidence? evidence)
    {
        if (evidence is not { Successful: true, SchemaVersion: "2" }) return [];
        if (evidence.SourceFingerprint != source || evidence.ConfigurationFingerprint != config) return [];
        return evidence.Methods.GroupBy(m => m.MethodId, StringComparer.Ordinal).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
    }

    private static Assessment Assess(MethodFact method, QualityConfig config, MethodCoverage? report)
    {
        var fraction = report is null ? null : CoverageFraction(report);
        var crap = fraction is { } value ? (double)method.Complexity * method.Complexity * Math.Pow(1 - value, 3) + method.Complexity : (double?)null;
        var findings = new List<Finding>();
        if (method.Complexity > config.ComplexityThreshold)
            findings.Add(ForMethod("code.complexity", method,
                $"Cyclomatic complexity {method.Complexity} exceeds {config.ComplexityThreshold}.",
                "Simplify decisions or extract cohesive behavior, then rerun this check."));
        if (crap > config.CrapThreshold)
            findings.Add(ForMethod("code.crap", method,
                FormattableString.Invariant($"CRAP {crap:0.###} exceeds {config.CrapThreshold}."),
                "Improve branch coverage and reduce decision complexity."));
        return new(method, fraction, crap, findings.ToArray()) { Report = report };
    }

    private static SortedDictionary<string, double?> Metrics(IEnumerable<Assessment> assessments)
    {
        var result = RuleContext.Metrics();
        foreach (var assessment in assessments)
        {
            result[$"method:{assessment.Method.Id}:complexity"] = assessment.Method.Complexity;
            result[$"method:{assessment.Method.Id}:coverage"] = assessment.Coverage;
            result[$"method:{assessment.Method.Id}:crap"] = assessment.Crap;
            result[$"method:{assessment.Method.Id}:coveredBranches"] = assessment.Report?.CoveredBranches;
            result[$"method:{assessment.Method.Id}:totalBranches"] = assessment.Report?.TotalBranches;
            result[$"method:{assessment.Method.Id}:coveredLines"] = assessment.Report?.CoveredLines;
            result[$"method:{assessment.Method.Id}:totalLines"] = assessment.Report?.TotalLines;
        }
        return result;
    }

    private static double? CoverageFraction(MethodCoverage report)
    {
        if (!ValidCount(report.CoveredBranches, report.TotalBranches)) return null;
        if (!ValidCount(report.CoveredLines, report.TotalLines)) return null;
        if (report.TotalBranches > 0) return (double)report.CoveredBranches / report.TotalBranches;
        return report.TotalLines > 0 ? (double)report.CoveredLines / report.TotalLines : null;
    }

    private static bool ValidCount(int covered, int total) => covered >= 0 && total >= covered;

    internal static Finding ForMethod(string rule, MethodFact method, string message, string advice) =>
        RuleContext.Finding(new() { Rule = rule, Key = method.Id, Message = message + " Method: " + method.Id, Advice = advice,
            Entities = [method.TypeId], Locations = [method.Location] });
}
