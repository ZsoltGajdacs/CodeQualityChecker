using Cqc.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Diagnostic = Cqc.Core.Diagnostic;

namespace Cqc.CSharp;

/// <summary>Reads source and cached metadata only. Never evaluates MSBuild or executes target code.</summary>
public sealed class CSharpAnalyzer : ILanguageAnalyzer, ILanguageExecution, ILanguageDiscovery
{
    public string Language => "csharp";
    public LanguageDefinition Definition => CSharpDiscovery.Definition;

    public Task<LanguageBuildResult> ExecuteAsync(ProjectSnapshot snapshot, string scratchDirectory, CancellationToken cancellationToken) =>
        new CSharpExecution().ExecuteAsync(snapshot, scratchDirectory, cancellationToken);

    public MethodCoverage[] ReadCoverage(LanguageBuildResult build, IEnumerable<MethodFact> methods) =>
        new CSharpExecution().ReadCoverage(build, methods);

    public async Task<AnalysisFacts> AnalyzeAsync(ProjectSnapshot snapshot, CancellationToken cancellationToken)
    {
        var sources = await SourceParsing.ReadAsync(snapshot, cancellationToken);
        var diagnostics = new List<Diagnostic>(sources.Diagnostics);
        var trees = sources.Trees;
        if (trees.Length == 0) return new([], [], [], diagnostics.ToArray(), diagnostics.Count == 0);

        var references = CachedReferences.Read(snapshot, diagnostics);
        var verifiedContext = sources.Consistent && sources.Options.All(x => x.Complete) && diagnostics.Count == 0;
        var compilation = Compilation(sources, references, cancellationToken);
        diagnostics.AddRange(CompilationDiagnostics(compilation, cancellationToken));
        var collector = Collect(sources, compilation, verifiedContext, cancellationToken);
        var types = collector.Types;
        var methods = collector.Methods;
        var dependencies = collector.Dependencies;
        var knownTypes = types.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var internalDependencies = dependencies.Where(x => knownTypes.Contains(x.To))
            .DistinctBy(x => (x.From, x.To, x.Location.File, x.Location.StartLine)).OrderBy(x => x.From, StringComparer.Ordinal)
            .ThenBy(x => x.To, StringComparer.Ordinal).ThenBy(x => x.Location.File, StringComparer.Ordinal).ThenBy(x => x.Location.StartLine).ToArray();
        var gaps = SourceEvidence.Gaps(sources, cancellationToken);
        var semanticComplete = diagnostics.Count == 0;
        diagnostics.AddRange(gaps.Where(gap => gap.Code == "csharp.unmodeled_executable")
            .Select(gap => new Diagnostic(gap.Code, "Executable initializers, primary constructors, or top-level statements are not modeled; structural scores remain incomplete.", gap.File, Language)));
        return new(types.OrderBy(x => x.Location.File, StringComparer.Ordinal).ThenBy(x => x.Location.StartLine).ToArray(),
            methods.OrderBy(x => x.Location.File, StringComparer.Ordinal).ThenBy(x => x.Location.StartLine).ToArray(),
            internalDependencies, diagnostics.ToArray(), semanticComplete)
        {
            Contexts = [new(SourceEvidence.ContextId, ".", "compilation", [], verifiedContext,
                verifiedContext ? [] : ["context.unverified"])],
            SyntaxGaps = gaps
        };
    }

    private static IEnumerable<Diagnostic> CompilationDiagnostics(CSharpCompilation compilation, CancellationToken token) =>
        compilation.GetDiagnostics(token).Where(x => x.Severity == DiagnosticSeverity.Error).Select(error =>
            new Diagnostic(error.Id, error.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                error.Location.IsInSource ? error.Location.SourceTree?.FilePath : null, "csharp"));

    private static FactCollector Collect(ParsedSources sources, CSharpCompilation compilation, bool verifiedContext, CancellationToken token)
    {
        var collector = new FactCollector();
        for (var index = 0; index < sources.Trees.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var tree = sources.Trees[index];
            collector.Collect(tree, new(compilation.GetSemanticModel(tree), token,
                sources.Options[index] with { Complete = sources.Consistent && sources.Options[index].Complete }, verifiedContext));
        }
        return collector;
    }

    private static CSharpCompilation Compilation(ParsedSources sources, MetadataReference[] references, CancellationToken token)
    {
        var trees = sources.Imports is null ? sources.Trees : sources.Trees.Append(sources.Imports);
        var topLevel = sources.Trees.Any(tree => tree.GetRoot(token).DescendantNodes().OfType<GlobalStatementSyntax>().Any());
        return CSharpCompilation.Create("CqcSourceAnalysis", trees, references,
            new CSharpCompilationOptions(topLevel ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
    }
}

internal static class SymbolNames
{
    private static readonly SymbolDisplayFormat Types = new(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.ExpandNullable);
    public static string Type(ITypeSymbol symbol) => symbol.OriginalDefinition.ToDisplayString(Types);
    public static string Method(IMethodSymbol symbol)
    {
        symbol = (symbol.ReducedFrom ?? symbol).OriginalDefinition;
        var name = symbol.MethodKind switch
        {
            MethodKind.Constructor => ".ctor",
            MethodKind.StaticConstructor => ".cctor",
            _ => symbol.MetadataName
        };
        var owner = symbol.MethodKind == MethodKind.LocalFunction && symbol.ContainingSymbol is IMethodSymbol enclosing
            ? Method(enclosing) : Type(symbol.ContainingType);
        return $"{owner}.{name}({string.Join(",", symbol.Parameters.Select(x => (x.RefKind == RefKind.None ? "" : x.RefKind.ToString().ToLowerInvariant() + " ") + x.Type.ToDisplayString(Types)))})";
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
