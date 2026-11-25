using Cqc.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cqc.CSharp;

internal sealed class FactCollector
{
    public List<TypeFact> Types { get; } = [];
    public List<MethodFact> Methods { get; } = [];
    public List<DependencyFact> Dependencies { get; } = [];
    public List<ClassDeclarationFact> ClassDeclarations { get; } = [];

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
        var syntaxComplete = SourceEvidence.HasCompleteSyntax(declaration, context);
        var evidence = SourceEvidence.Type(declaration, context, syntaxComplete);
        var complete = evidence.Syntax && evidence.Dependencies && evidence.VerifiedContext;
        var id = context.Identity.OwnType(symbol);
        var location = SourceLocation.Of(declaration);
        var type = new TypeFact(id, "csharp", location,
            symbol.IsAbstract || symbol.TypeKind == TypeKind.Interface)
        {
            IsTest = isTest, Complete = complete, ContextId = context.ContextId,
            QualifiedName = ProjectSymbolIdentity.TypeName(symbol),
            NamespaceName = ProjectSymbolIdentity.NamespaceName(symbol),
            Evidence = evidence
        };
        Types.Add(type);
        CollectClassDeclaration(type, declaration, symbol.TypeKind, syntaxComplete);
        foreach (var method in MethodDeclarations.InType(declaration))
        {
            if (MethodDeclarations.Symbol(method, context.Model, context.Token) is { } methodSymbol)
            {
                Methods.Add(MethodFacts.Create(method, methodSymbol, context, isTest));
            }
        }
        if (!context.SourceOnly) Dependencies.AddRange(TypeDependencies.Read(declaration, symbol, context));
    }

    private void CollectClassDeclaration(TypeFact type, BaseTypeDeclarationSyntax declaration, TypeKind kind, bool syntaxComplete)
    {
        if (type.IsTest || kind != TypeKind.Class) return;
        if (declaration is TypeDeclarationSyntax classDeclaration)
            ClassDeclarations.Add(new(type.Id, type.Language, type.Location, MaxConstructorArity(classDeclaration), syntaxComplete));
    }

    private static int MaxConstructorArity(TypeDeclarationSyntax declaration) => declaration.Members
        .OfType<ConstructorDeclarationSyntax>().Select(constructor => constructor.ParameterList.Parameters.Count)
        .Append(declaration.ParameterList?.Parameters.Count ?? 0).Max();
}

internal sealed record SourceAnalysisContext(SemanticModel Model, CancellationToken Token, ProjectCompilation Project)
{
    public SourceOptions Options => Project.Source.Options;
    public ProjectSymbolIdentity Identity => Project.Identity;
    public string ContextId => Project.Context.Id;
    public bool VerifiedContext => Project.Context.Verified;
    public bool SourceOnly => Project.SourceOnly;
}
