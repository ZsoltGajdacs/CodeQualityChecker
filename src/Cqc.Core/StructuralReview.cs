namespace Cqc.Core;

internal static class StructuralReview
{
    internal static FamilyResult[] Evaluate(RuleContext context, QualityConfig config) =>
        new[] { Complexity(context, config), DuplicationRules.Evaluate(context, config) }
            .Where(family => family.Entities.Length != 0).ToArray();

    private static FamilyResult Complexity(RuleContext context, QualityConfig config)
    {
        var methodsByType = ProductionRules.ProductionMethods(context).ToLookup(method => method.TypeId, StringComparer.Ordinal);
        var unknownFiles = context.Facts.SyntaxGaps.Where(gap => gap.UnknownEntities)
            .Select(gap => gap.File).ToHashSet(StringComparer.Ordinal);
        var productionTypes = context.Facts.Types.Where(type => !type.IsTest).ToArray();
        var blocked = productionTypes.Where(type => unknownFiles.Contains(type.Location.File)
                || AssessmentEvidence.Syntax(type, methodsByType[type.Id]).Length != 0)
            .Select(type => type.Id).ToHashSet(StringComparer.Ordinal);
        var blockedFiles = productionTypes.Where(type => blocked.Contains(type.Id))
            .Select(type => type.Location.File).ToHashSet(StringComparer.Ordinal);
        var types = productionTypes.Where(type => !blocked.Contains(type.Id) && methodsByType.Contains(type.Id)).ToArray();
        var findings = types.Select(type => type.Id).Distinct(StringComparer.Ordinal).SelectMany(id => methodsByType[id])
            .Select(method => ProductionRules.ComplexityFinding(method, config)).OfType<Finding>();
        var report = new EntityReport(new()
        {
            Family = "code", Types = types, Findings = findings, StaticBlockers = _ => [],
            Metrics = id => ComplexityMetrics(methodsByType[id])
        }).Build(context.Snapshot.SelectedFiles);
        var entities = report.Entities.Where(entity => entity.Kind != "file" || !blockedFiles.Contains(entity.File))
            .Select(entity => entity with { Score = null }).ToArray();
        return new("code", "complete", entities, report.Findings, []);
    }

    private static SortedDictionary<string, double?> ComplexityMetrics(IEnumerable<MethodFact> methods)
    {
        var metrics = RuleContext.Metrics();
        foreach (var method in methods) metrics[$"method:{method.Id}:complexity"] = method.Complexity;
        return metrics;
    }

    internal static IEnumerable<Diagnostic> Diagnostics(AnalysisFacts facts, string[] selectedFiles)
    {
        var languages = selectedFiles.Select(file => Path.GetExtension(file).ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        return facts.Diagnostics.Where(IsAnalyzerFailure)
            .Concat(facts.SyntaxGaps.Select(gap => new Diagnostic(gap.Code, "Source structure could not be fully analyzed.",
                gap.File, gap.Language)))
            .Where(diagnostic => diagnostic.Language switch
            {
                "csharp" => languages.Contains(".cs"),
                "java" => languages.Contains(".java"),
                _ => true
            }).Distinct();
    }

    internal static bool IsAnalyzerFailure(Diagnostic diagnostic) => diagnostic.Code is
        "analyzer_failed" or "java.worker_missing" or "java.worker_failed" or "java.worker_protocol" or "java.worker_empty";
}
