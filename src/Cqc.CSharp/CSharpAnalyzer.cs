using Cqc.Core;
using Microsoft.CodeAnalysis;
using Diagnostic = Cqc.Core.Diagnostic;

namespace Cqc.CSharp;

/// <summary>Reads source and cached metadata only. Never evaluates MSBuild or executes target code.</summary>
public sealed class CSharpAnalyzer : ILanguageAnalyzer, ILanguageExecution, ILanguageDiscovery
{
    public string Language => "csharp";
    public string? CoverageIdentityVersion => CSharpExecution.MethodIdentityVersion;
    public LanguageDefinition Definition => CSharpDiscovery.Definition;

    public Task<LanguageBuildResult> ExecuteAsync(ProjectSnapshot snapshot, string runDirectory, CancellationToken cancellationToken) =>
        new CSharpExecution().ExecuteAsync(snapshot, runDirectory, cancellationToken);

    public MethodCoverage[] ReadCoverage(LanguageBuildResult build, IEnumerable<MethodFact> methods) =>
        new CSharpExecution().ReadCoverage(build, methods);

    public async Task<AnalysisFacts> AnalyzeAsync(ProjectSnapshot snapshot, CancellationToken cancellationToken)
    {
        var sources = await SourceParsing.ReadAsync(snapshot, cancellationToken);
        var projects = new ProjectCompilations(snapshot, sources, cancellationToken).Build();
        var collector = Collect(projects, cancellationToken);
        var diagnostics = projects.SelectMany(project => project.Diagnostics).Distinct().ToList();
        var semanticComplete = diagnostics.Count == 0;
        var gaps = sources.SelectMany(source => SourceEvidence.Gaps(source, cancellationToken)).Distinct().ToArray();
        diagnostics.AddRange(gaps.Where(gap => gap.Code == "csharp.unmodeled_executable")
            .Select(gap => new Diagnostic(gap.Code, "Executable initializers, primary constructors, or top-level statements are not modeled; structural scores remain incomplete.", gap.File, Language)));
        return new(collector.Types.OrderBy(type => type.Location.File, StringComparer.Ordinal).ThenBy(type => type.Location.StartLine).ToArray(),
            collector.Methods.OrderBy(method => method.Location.File, StringComparer.Ordinal).ThenBy(method => method.Location.StartLine).ToArray(),
            InternalDependencies(collector), diagnostics.ToArray(), semanticComplete)
        {
            Contexts = projects.Select(project => project.Context).ToArray(),
            SyntaxGaps = gaps
        };
    }

    private static FactCollector Collect(ProjectCompilation[] projects, CancellationToken token)
    {
        var collector = new FactCollector();
        foreach (var project in projects)
            foreach (var tree in project.Source.Trees)
            {
                token.ThrowIfCancellationRequested();
                collector.Collect(tree, new(project.Compilation.GetSemanticModel(tree), token, project));
            }
        return collector;
    }

    private static DependencyFact[] InternalDependencies(FactCollector collector)
    {
        var known = collector.Types.Select(type => type.Id).ToHashSet(StringComparer.Ordinal);
        return collector.Dependencies.Where(edge => known.Contains(edge.To))
            .Distinct()
            .OrderBy(edge => edge.From, StringComparer.Ordinal).ThenBy(edge => edge.To, StringComparer.Ordinal)
            .ThenBy(edge => edge.Location.File, StringComparer.Ordinal).ThenBy(edge => edge.Location.StartLine).ToArray();
    }
}

internal static class SourceLocation
{
    public static Cqc.Core.Location Of(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        return new(span.Path.Replace('\\', '/'), span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1);
    }
}
