using Cqc.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cqc.CSharp;

internal sealed class FactCollector
{
    public List<TypeFact> Types { get; } = [];
    public List<MethodFact> Methods { get; } = [];
    public List<DependencyFact> Dependencies { get; } = [];

    public void Collect(SyntaxTree tree, SemanticModel model, CancellationToken token, SourceFileOptions options)
    {
        var context = new TreeContext(model, token, options);
        foreach (var declaration in tree.GetRoot(token).DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(declaration, token) is INamedTypeSymbol symbol)
                CollectType(declaration, symbol, context);
        }
    }

    private void CollectType(BaseTypeDeclarationSyntax declaration, INamedTypeSymbol symbol, TreeContext context)
    {
        var isTest = context.Options.TestProject || TestRecognition.IsTestType(symbol);
        var complete = context.Options.Complete && !context.Model.GetDiagnostics(declaration.Span, context.Token).Any(x => x.Severity == DiagnosticSeverity.Error);
        Types.Add(new(SymbolNames.Type(symbol), "csharp", SourceLocation.Of(declaration), symbol.IsAbstract || symbol.TypeKind == TypeKind.Interface, isTest, complete));
        foreach (var method in MethodDeclarations.InType(declaration))
        {
            if (MethodDeclarations.Symbol(method, context.Model, context.Token) is { } methodSymbol)
            {
                var fact = MethodFacts.Create(method, methodSymbol, new(context.Model, isTest, context.Token));
                Methods.Add(fact with { Complete = fact.Complete && complete });
            }
        }
        Dependencies.AddRange(TypeDependencies.Read(declaration, symbol, context.Model, context.Token));
    }

    private sealed record TreeContext(SemanticModel Model, CancellationToken Token, SourceFileOptions Options);
}
