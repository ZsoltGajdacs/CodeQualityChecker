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
        var methods = StaticComplexity.ProductionMethods(context).ToArray();
        var staticComplexity = new StaticComplexity(context);
        var types = context.TypesFor(methods)
            .Concat(context.Facts.Types.Where(type => !type.IsTest
                && (!AssessmentEvidence.Of(type).Syntax || staticComplexity.HasExecutables(type.Id))))
            .Distinct().ToArray();
        if (methods.Length == 0 && types.Length == 0)
            return context.Finish(new() { Family = "code", Types = types, Findings = [] });
        var coverage = MatchingCoverage(context.Snapshot.SourceFingerprint, configFingerprint, evidence);
        var assessments = methods.Select(m =>
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return Assess(m, config, coverage.GetValueOrDefault(m.Id));
        }).ToArray();
        var executableFindings = staticComplexity.ExecutableFindings(config).ToArray();
        return context.Finish(new()
        {
            Family = "code", Types = types, Findings = assessments.SelectMany(a => a.Findings).Concat(executableFindings),
            StaticOnlyFindingIds = executableFindings.Select(finding => finding.Id).ToHashSet(StringComparer.Ordinal),
            Diagnostics = CoverageDiagnostics(assessments, context.ChangedCode),
            Contributions = assessments.Select(Contribution).Concat(staticComplexity.Contributions(sourceOnly: false)),
            IncludeTouchedDeclaration = (type, scope) => scope.Includes(type.Location) && staticComplexity.DeclarationBlockers(type).Length != 0,
            Aggregate = selected => selected.Assess(
                primary: selected.Contributions.Any(entry => entry.Primary)
                    || (selected.Contributions.Length == 0 && !staticComplexity.HasExecutables(selected.TypeId)),
                staticScore: true,
                declarationBlockers: selected.Declarations.SelectMany(type => AssessmentEvidence.Syntax(type, [])),
                staticDeclarationBlockers: selected.Declarations.SelectMany(staticComplexity.DeclarationBlockers))
        });
    }

    private static Diagnostic[] CoverageDiagnostics(IEnumerable<Assessment> assessments, ChangedCodeScope? scope) =>
        assessments.Any(a => !a.Complete && (scope?.Includes(a.Method.Location) ?? true))
            ? [new("coverage.incomplete", "Combined scoring requires complete syntax, resolved method identities and source/configuration-matched coverage. Inspect entity blockers; execute=true can collect coverage but may not resolve every blocker.")]
            : [];

    private static ScoringContribution Contribution(Assessment assessment) => new(assessment.Method.TypeId, assessment.Method.Location)
    {
        Static = true,
        Blockers = assessment.Blockers,
        StaticBlockers = StaticComplexity.SyntaxBlockers(AssessmentEvidence.Of(assessment.Method).Syntax),
        Metrics = Metrics([assessment])
    };

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
        if (ComplexityFinding(method, config) is { } staticFinding) findings.Add(staticFinding);
        if (crap > config.CrapThreshold)
            findings.Add(ForMethod("code.crap", method,
                FormattableString.Invariant($"CRAP {crap:0.###} exceeds {config.CrapThreshold}."),
                "Improve branch coverage and reduce decision complexity."));
        return new(method, fraction, crap, findings.ToArray()) { Report = report };
    }

    internal static Finding? ComplexityFinding(MethodFact method, QualityConfig config) =>
        StaticComplexity.Finding(StructuralExecutable.FromMethod(method), config);

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
