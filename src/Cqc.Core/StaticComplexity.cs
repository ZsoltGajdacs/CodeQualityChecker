namespace Cqc.Core;

/// <summary>Static complexity evidence shared by source-only and coverage-aware reviews.</summary>
internal sealed class StaticComplexity(RuleContext context)
{
    private readonly ILookup<string, StructuralExecutable> executables = ProductionExecutables(context)
        .ToLookup(executable => executable.TypeId, StringComparer.Ordinal);

    internal bool HasExecutables(string typeId) => executables.Contains(typeId);

    internal string[] DeclarationBlockers(TypeFact type) => context.Structure.Blockers(type, []);

    internal IEnumerable<ScoringContribution> Contributions(bool sourceOnly) => executables.SelectMany(group => group)
        .Where(executable => sourceOnly || !executable.IsMethod).Select(executable => new ScoringContribution(executable.TypeId, executable.Location)
        {
            Primary = sourceOnly,
            Static = true,
            Blockers = SyntaxBlockers(executable.Syntax),
            StaticBlockers = SyntaxBlockers(executable.Syntax),
            Metrics = new(StringComparer.Ordinal) { [MetricName(executable)] = executable.Complexity }
        });

    internal static string[] SyntaxBlockers(bool syntax) => syntax ? [] : ["syntax.incomplete"];

    internal IEnumerable<Finding> ExecutableFindings(QualityConfig config) =>
        executables.SelectMany(group => group).Where(executable => !executable.IsMethod)
            .Select(executable => Finding(executable, config)).OfType<Finding>();

    internal IEnumerable<Finding> Findings(string typeId, QualityConfig config) =>
        executables[typeId].Where(executable => executable.Syntax)
            .Select(executable => Finding(executable, config)).OfType<Finding>();

    private static string MetricName(StructuralExecutable executable) =>
        $"{(executable.IsMethod ? "method" : "executable")}:{executable.Id}:complexity";

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
        var methods = ProductionMethods(context).Select(method => method.Id).ToHashSet(StringComparer.Ordinal);
        return context.Index.Executables.Where(executable => executable.IsMethod ? methods.Contains(executable.Id)
            : context.Index.TypesById[executable.TypeId].Any(type => !type.IsTest));
    }

    internal static IEnumerable<MethodFact> ProductionMethods(RuleContext context)
    {
        var testTypes = context.Facts.Types.Where(type => type.IsTest).Select(type => type.Id).ToHashSet(StringComparer.Ordinal);
        return context.Methods.Where(method => !method.IsTest && !method.IsFixture && !testTypes.Contains(method.TypeId));
    }
}
