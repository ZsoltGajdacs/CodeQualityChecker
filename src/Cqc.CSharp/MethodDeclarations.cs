using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cqc.CSharp;

internal static class MethodDeclarations
{
    public static IEnumerable<SyntaxNode> InType(BaseTypeDeclarationSyntax type) => type.DescendantNodes()
        .Where(x => x is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax or
            PropertyDeclarationSyntax or IndexerDeclarationSyntax)
        .Where(x => ReferenceEquals(x.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault(), type))
        .Where(node => Body(node) is not null);

    public static SyntaxNode? Body(SyntaxNode node) => node switch
    {
        BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression,
        AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression,
        LocalFunctionStatementSyntax local => (SyntaxNode?)local.Body ?? local.ExpressionBody?.Expression,
        PropertyDeclarationSyntax property => property.ExpressionBody?.Expression,
        IndexerDeclarationSyntax indexer => indexer.ExpressionBody?.Expression,
        _ => null
    };

    public static IEnumerable<SyntaxNode> BodyNodes(SyntaxNode method) => method.DescendantNodes(descendIntoChildren: node =>
        ReferenceEquals(node, method) || OwnsChildren(node));

    public static IEnumerable<SyntaxToken> BodyCodeTokens(SyntaxNode body) =>
        body.DescendantTokens(descendIntoChildren: OwnsChildren)
            .Where(token => !token.IsMissing && token.Kind() is not (SyntaxKind.OpenBraceToken or SyntaxKind.CloseBraceToken));

    private static bool OwnsChildren(SyntaxNode node) => node is not (LocalFunctionStatementSyntax or BaseTypeDeclarationSyntax);

    public static IMethodSymbol? Symbol(SyntaxNode declaration, SemanticModel model, CancellationToken token) =>
        model.GetDeclaredSymbol(declaration, token) switch
        {
            IMethodSymbol method => method,
            IPropertySymbol property => property.GetMethod,
            _ => null
        };
}
