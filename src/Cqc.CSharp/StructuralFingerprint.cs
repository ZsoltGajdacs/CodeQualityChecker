using Cqc.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cqc.CSharp;

internal static class StructuralFingerprint
{
    public static string[] Create(SyntaxNode[] nodes) => nodes.Where(x => x is ExpressionSyntax or StatementSyntax)
        .Select(x => Wire.Hash(string.Join(" ", x.DescendantTokens().Select(Normalize))))
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static string Normalize(SyntaxToken token)
    {
        if (token.Parent is LiteralExpressionSyntax) return token.Parent.Kind().ToString();
        if (!token.IsKind(SyntaxKind.IdentifierToken)) return token.Kind().ToString();
        return IsCalledName(token.Parent) ? token.ValueText : "identifier";
    }

    private static bool IsCalledName(SyntaxNode? node) => node is SimpleNameSyntax name &&
        (name.Parent is InvocationExpressionSyntax invocation && ReferenceEquals(invocation.Expression, name) ||
         name.Parent is MemberAccessExpressionSyntax member && ReferenceEquals(member.Name, name) && member.Parent is InvocationExpressionSyntax);
}
