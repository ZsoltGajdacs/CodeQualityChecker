using Cqc.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cqc.CSharp;

internal static class TypeDependencies
{
    public static IEnumerable<DependencyFact> Read(BaseTypeDeclarationSyntax declaration, INamedTypeSymbol owner,
        SourceAnalysisContext context)
    {
        var from = context.Identity.OwnType(owner);
        foreach (var node in declaration.DescendantNodes().Where(x =>
                     ReferenceEquals(x.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault(), declaration)))
        {
            context.Token.ThrowIfCancellationRequested();
            foreach (var target in ReferencedTypes(node, context.Model, context.Token).Where(x => x.TypeKind != TypeKind.Error))
            {
                var to = context.Identity.ReferencedType(target);
                if (to is not null && from != to) yield return new(from, to, SourceLocation.Of(node));
            }
        }
    }

    private static IEnumerable<ITypeSymbol> ReferencedTypes(SyntaxNode node, SemanticModel model, CancellationToken token)
    {
        if (node is TypeSyntax type && model.GetTypeInfo(type, token).Type is { } target) yield return target;
        if (node is ExpressionSyntax && model.GetSymbolInfo(node, token).Symbol?.ContainingType is { } owner) yield return owner;
    }
}
