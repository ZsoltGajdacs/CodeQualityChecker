using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cqc.CSharp;

/// <summary>Recognizes direct parameter-to-field wiring without resolving dependency types.</summary>
internal static class ConstructorWiring
{
    public static bool IsOnlyWiring(SyntaxNode declaration, IMethodSymbol symbol)
    {
        if (declaration is not ConstructorDeclarationSyntax constructor) return false;
        if (constructor.Initializer != null || constructor.ContainsDiagnostics) return false;
        var parameters = constructor.ParameterList.Parameters.Select(parameter => parameter.Identifier.ValueText).ToArray();
        if (parameters.Distinct(StringComparer.Ordinal).Count() != parameters.Length) return false;
        var expressions = Assignments(constructor).ToArray();
        return expressions.Length > 0 && expressions.All(expression => IsWiring(expression, parameters, symbol.ContainingType));
    }

    private static IEnumerable<ExpressionSyntax?> Assignments(ConstructorDeclarationSyntax constructor)
    {
        if (constructor.ExpressionBody != null) return [constructor.ExpressionBody.Expression];
        return constructor.Body?.Statements.Select(statement => (statement as ExpressionStatementSyntax)?.Expression) ?? [];
    }

    private static bool IsWiring(ExpressionSyntax? expression, string[] parameters, INamedTypeSymbol type)
    {
        if (expression is not AssignmentExpressionSyntax assignment) return false;
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)) return false;
        if (assignment.Right is not IdentifierNameSyntax parameter) return false;
        if (!parameters.Contains(parameter.Identifier.ValueText, StringComparer.Ordinal)) return false;
        var name = FieldName(assignment.Left, parameters);
        if (name == null) return false;
        var members = type.GetMembers(name);
        return members.Length == 1 && members[0] is IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false };
    }

    private static string? FieldName(ExpressionSyntax expression, string[] parameters) => expression switch
    {
        MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax name } => name.Identifier.ValueText,
        IdentifierNameSyntax name when !parameters.Contains(name.Identifier.ValueText, StringComparer.Ordinal) => name.Identifier.ValueText,
        _ => null
    };
}
