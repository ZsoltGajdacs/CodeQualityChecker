namespace Cqc.Core;

/// <summary>Static complexity evidence shared by source-only and coverage-aware reviews.</summary>
internal sealed class StaticComplexity(RuleContext context)
{
    private readonly ILookup<string, StructuralExecutable> executables = ProductionExecutables(context)
        .ToLookup(executable => executable.TypeId, StringComparer.Ordinal);

    internal bool HasExecutables(string typeId) => executables.Contains(typeId);

    internal string[] Blockers(TypeFact type) => context.Structure.Blockers(type, executables[type.Id]);

    internal IEnumerable<Finding> InitializerFindings(QualityConfig config) =>
        executables.SelectMany(group => group).Where(executable => !executable.IsMethod)
            .Select(executable => Finding(executable, config)).OfType<Finding>();

    internal IEnumerable<Finding> Findings(string typeId, QualityConfig config) =>
        executables[typeId].Where(executable => executable.Syntax)
            .Select(executable => Finding(executable, config)).OfType<Finding>();

    internal SortedDictionary<string, double?> Metrics(string typeId) => Metrics(typeId, includeMethods: true);

    internal SortedDictionary<string, double?> Metrics(string typeId, SortedDictionary<string, double?>? methods)
    {
        var metrics = Metrics(typeId, includeMethods: false);
        if (methods is not null)
            foreach (var (name, value) in methods) metrics.Add(name, value);
        return metrics;
    }

    private SortedDictionary<string, double?> Metrics(string typeId, bool includeMethods)
    {
        var metrics = RuleContext.Metrics();
        foreach (var executable in executables[typeId].Where(e => includeMethods || !e.IsMethod))
        {
            var prefix = executable.IsMethod ? "method" : "executable";
            metrics[$"{prefix}:{executable.Id}:complexity"] = executable.Complexity;
        }
        return metrics;
    }

    internal static Finding? Finding(StructuralExecutable executable, QualityConfig config) =>
        executable.Complexity > config.ComplexityThreshold ? RuleContext.Finding(new()
        {
            Rule = "code.complexity",
            Key = executable.Id,
            Message = $"Cyclomatic complexity {executable.Complexity} exceeds {config.ComplexityThreshold}. "
                + (executable.IsMethod ? "Method: " : "Executable: ") + executable.Id,
            Advice = "Simplify decisions or extract cohesive behavior, then rerun this check.",
            Entities = [executable.TypeId],
            Locations = [executable.Location]
        }) : null;

    private static IEnumerable<StructuralExecutable> ProductionExecutables(RuleContext context)
    {
        var methods = ProductionRules.ProductionMethods(context).Select(method => method.Id).ToHashSet(StringComparer.Ordinal);
        return context.Index.Executables.Where(executable => executable.IsMethod ? methods.Contains(executable.Id)
            : context.Index.TypesById[executable.TypeId].Any(type => !type.IsTest));
    }
}
