using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cqc.CSharp;

/// <summary>Syntax measurements remain available when target symbols cannot be resolved.</summary>
internal sealed record MethodMeasurement
{
    public int Complexity { get; init; }
    public int Lines { get; init; }
    public required string[] Fingerprints { get; init; }
    public int NodeCount { get; init; }

    public static MethodMeasurement Read(SyntaxNode[] nodes)
    {
        var lines = nodes.OfType<StatementSyntax>().Where(x => x is not BlockSyntax and not LocalFunctionStatementSyntax)
            .SelectMany(x => x.DescendantTokens().Where(t => !t.IsKind(SyntaxKind.OpenBraceToken) && !t.IsKind(SyntaxKind.CloseBraceToken))
                .Select(t => t.GetLocation().GetLineSpan().StartLinePosition.Line)).Distinct().Count();
        if (lines == 0 && nodes.OfType<ArrowExpressionClauseSyntax>().Any()) lines = 1;
        return new() { Complexity = 1 + nodes.Count(IsDecision), Lines = lines,
            Fingerprints = StructuralFingerprint.Create(nodes), NodeCount = nodes.Length };
    }

    private static bool IsDecision(SyntaxNode node) => node is IfStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or
        ForEachVariableStatementSyntax or WhileStatementSyntax or DoStatementSyntax or CatchClauseSyntax or ConditionalExpressionSyntax or
        CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax or SwitchExpressionArmSyntax ||
        node is BinaryExpressionSyntax binary && binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression or SyntaxKind.CoalesceExpression ||
        node is ConditionalAccessExpressionSyntax;
}
