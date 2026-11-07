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
        var imports = sources.Imports;
        var compilationTrees = imports is null ? trees : trees.Append(imports);
        var compilation = CSharpCompilation.Create("CqcSourceAnalysis", compilationTrees, references,
            new CSharpCompilationOptions(trees.Any(x => x.GetRoot(cancellationToken).DescendantNodes().OfType<GlobalStatementSyntax>().Any()) ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        var errors = compilation.GetDiagnostics(cancellationToken).Where(x => x.Severity == DiagnosticSeverity.Error).ToArray();
        foreach (var error in errors)
        {
            var file = error.Location.IsInSource ? error.Location.SourceTree?.FilePath : null;
            diagnostics.Add(new(error.Id, error.GetMessage(System.Globalization.CultureInfo.InvariantCulture), file, Language));
        }

        var collector = new FactCollector();
        for (var index = 0; index < trees.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tree = trees[index];
            collector.Collect(tree, compilation.GetSemanticModel(tree), cancellationToken,
                sources.Options[index] with { Complete = sources.Consistent && sources.Options[index].Complete });
        }
        var types = collector.Types;
        var methods = collector.Methods;
        var dependencies = collector.Dependencies;
        var knownTypes = types.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var internalDependencies = dependencies.Where(x => knownTypes.Contains(x.To))
            .DistinctBy(x => (x.From, x.To, x.Location.File, x.Location.StartLine)).OrderBy(x => x.From, StringComparer.Ordinal)
            .ThenBy(x => x.To, StringComparer.Ordinal).ThenBy(x => x.Location.File, StringComparer.Ordinal).ThenBy(x => x.Location.StartLine).ToArray();
        return new(types.OrderBy(x => x.Location.File, StringComparer.Ordinal).ThenBy(x => x.Location.StartLine).ToArray(),
            methods.OrderBy(x => x.Location.File, StringComparer.Ordinal).ThenBy(x => x.Location.StartLine).ToArray(),
            internalDependencies, diagnostics.ToArray(), diagnostics.Count == 0);
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
