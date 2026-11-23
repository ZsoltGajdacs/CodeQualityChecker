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
        Types.Add(new(context.Identity.OwnType(symbol), "csharp", SourceLocation.Of(declaration),
            symbol.IsAbstract || symbol.TypeKind == TypeKind.Interface)
        {
            IsTest = isTest, Complete = complete, ContextId = context.ContextId,
            QualifiedName = ProjectSymbolIdentity.TypeName(symbol), Evidence = evidence
        });
        foreach (var method in MethodDeclarations.InType(declaration))
        {
            if (MethodDeclarations.Symbol(method, context.Model, context.Token) is { } methodSymbol)
            {
                Methods.Add(MethodFacts.Create(method, methodSymbol, context, isTest));
            }
        }
        if (!context.SourceOnly) Dependencies.AddRange(TypeDependencies.Read(declaration, symbol, context));
    }

}

internal sealed record SourceAnalysisContext(SemanticModel Model, CancellationToken Token, ProjectCompilation Project)
{
    public SourceOptions Options => Project.Source.Options;
    public ProjectSymbolIdentity Identity => Project.Identity;
    public string ContextId => Project.Context.Id;
    public bool VerifiedContext => Project.Context.Verified;
    public bool SourceOnly => Project.SourceOnly;
}
