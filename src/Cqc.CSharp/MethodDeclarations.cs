using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cqc.CSharp;

internal static class MethodDeclarations
{
    public static IEnumerable<SyntaxNode> InType(BaseTypeDeclarationSyntax type) => type.DescendantNodes()
        .Where(x => x is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax or
            PropertyDeclarationSyntax or IndexerDeclarationSyntax)
        .Where(x => ReferenceEquals(x.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault(), type))
        .Where(HasBody);

    public static bool HasBody(SyntaxNode node) => node switch
    {
        BaseMethodDeclarationSyntax method => method.Body != null || method.ExpressionBody != null,
        AccessorDeclarationSyntax accessor => accessor.Body != null || accessor.ExpressionBody != null,
        LocalFunctionStatementSyntax local => local.Body != null || local.ExpressionBody != null,
        PropertyDeclarationSyntax property => property.ExpressionBody != null,
        IndexerDeclarationSyntax indexer => indexer.ExpressionBody != null,
        _ => false
    };

    public static IEnumerable<SyntaxNode> BodyNodes(SyntaxNode method) => method.DescendantNodes(descendIntoChildren: node =>
        ReferenceEquals(node, method) || node is not (LocalFunctionStatementSyntax or BaseTypeDeclarationSyntax));

    public static IMethodSymbol? Symbol(SyntaxNode declaration, SemanticModel model, CancellationToken token) =>
        model.GetDeclaredSymbol(declaration, token) switch
        {
            IMethodSymbol method => method,
            IPropertySymbol property => property.GetMethod,
            _ => null
        };
}
