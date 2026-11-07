using Cqc.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cqc.CSharp;

internal static class MethodFacts
{
    public static MethodFact Create(SyntaxNode declaration, IMethodSymbol symbol, MethodAnalysisContext context)
    {
        var model = context.Model;
        var token = context.Token;
        var nodes = MethodDeclarations.BodyNodes(declaration).ToArray();
        var invocations = nodes.OfType<InvocationExpressionSyntax>().ToArray();
        var calls = invocations.Select(x => model.GetSymbolInfo(x, token).Symbol).OfType<IMethodSymbol>()
            .Select(SymbolNames.Method).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var lines = nodes.OfType<StatementSyntax>().Where(x => x is not BlockSyntax and not LocalFunctionStatementSyntax)
            .SelectMany(x => x.DescendantTokens().Where(t => !t.IsKind(SyntaxKind.OpenBraceToken) && !t.IsKind(SyntaxKind.CloseBraceToken))
                .Select(t => t.GetLocation().GetLineSpan().StartLinePosition.Line)).Distinct().Count();
        if (lines == 0 && nodes.OfType<ArrowExpressionClauseSyntax>().Any()) lines = 1;
        var complete = !model.GetDiagnostics(declaration.Span, token).Any(x => x.Severity == DiagnosticSeverity.Error) &&
            invocations.All(x => model.GetSymbolInfo(x, token).Symbol is IMethodSymbol);
        return new(SymbolNames.Method(symbol), SymbolNames.Type(symbol.ContainingType), "csharp", SourceLocation.Of(declaration),
            1 + nodes.Count(IsDecision), lines, TestRecognition.IsTest(symbol), TestRecognition.IsFixture(symbol, context.TestType),
            TestRecognition.IsParameterized(symbol), invocations.Count(x => TestRecognition.IsAssertion(x, model, token)), calls,
            StructuralFingerprint.Create(nodes), nodes.Length, complete);
    }

    private static bool IsDecision(SyntaxNode node) => node is IfStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or
        ForEachVariableStatementSyntax or WhileStatementSyntax or DoStatementSyntax or CatchClauseSyntax or ConditionalExpressionSyntax or
        CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax or SwitchExpressionArmSyntax ||
        node is BinaryExpressionSyntax binary && binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression or SyntaxKind.CoalesceExpression ||
        node is ConditionalAccessExpressionSyntax;
}

internal sealed record MethodAnalysisContext(SemanticModel Model, bool TestType, CancellationToken Token);
