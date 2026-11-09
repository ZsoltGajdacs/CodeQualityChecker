using Cqc.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cqc.CSharp;

internal sealed class FactCollector
{
    public List<TypeFact> Types { get; } = [];
    public List<MethodFact> Methods { get; } = [];
    public List<DependencyFact> Dependencies { get; } = [];

    public void Collect(SyntaxTree tree, SourceAnalysisContext context)
    {
        foreach (var declaration in tree.GetRoot(context.Token).DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            if (context.Model.GetDeclaredSymbol(declaration, context.Token) is INamedTypeSymbol symbol)
                CollectType(declaration, symbol, context);
        }
    }

    private void CollectType(BaseTypeDeclarationSyntax declaration, INamedTypeSymbol symbol, SourceAnalysisContext context)
    {
        var isTest = context.Options.TestProject || TestRecognition.IsTestType(symbol);
        var evidence = SourceEvidence.Type(declaration, context);
        var complete = evidence.Syntax && evidence.Dependencies && evidence.VerifiedContext;
        Types.Add(new(SymbolNames.Type(symbol), "csharp", SourceLocation.Of(declaration), symbol.IsAbstract || symbol.TypeKind == TypeKind.Interface)
        { IsTest = isTest, Complete = complete, ContextId = SourceEvidence.ContextId, QualifiedName = SymbolNames.Type(symbol), Evidence = evidence });
        foreach (var method in MethodDeclarations.InType(declaration))
        {
            if (MethodDeclarations.Symbol(method, context.Model, context.Token) is { } methodSymbol)
            {
                Methods.Add(MethodFacts.Create(method, methodSymbol, new(context.Model, isTest, context.Token,
                    context.Options.Complete, context.VerifiedContext)));
            }
        }
        Dependencies.AddRange(TypeDependencies.Read(declaration, symbol, context.Model, context.Token));
    }

}

internal sealed record SourceAnalysisContext(SemanticModel Model, CancellationToken Token, SourceFileOptions Options, bool VerifiedContext);
