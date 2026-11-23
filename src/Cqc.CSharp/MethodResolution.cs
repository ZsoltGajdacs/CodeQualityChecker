using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cqc.CSharp;

internal sealed record MethodResolution
{
    public required string[] Calls { get; init; }
    public int Assertions { get; init; }
    public bool IdentityComplete { get; init; }
    public bool CallsComplete { get; init; }

    public static MethodResolution Read(SyntaxNode declaration, SyntaxNode[] nodes, IMethodSymbol symbol,
        SourceAnalysisContext context)
    {
        var invocations = nodes.OfType<InvocationExpressionSyntax>().ToArray();
        var resolved = invocations.Select(x => context.Model.GetSymbolInfo(x, context.Token).Symbol as IMethodSymbol).ToArray();
        var calls = resolved.OfType<IMethodSymbol>().Concat(MethodGroups(nodes, context)).Select(context.Identity.ReferencedMethod).OfType<string>()
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        // Resolving Delegate.Invoke identifies the signature, not the runtime target or helper closure.
        var callsComplete = resolved.All(KnownTarget)
            && !context.Model.GetDiagnostics(declaration.Span, context.Token).Any(x => x.Severity == DiagnosticSeverity.Error);
        var identityComplete = symbol.Parameters.All(parameter => Resolved(parameter.Type));
        return new() { Calls = calls, Assertions = resolved.Count(TestRecognition.IsAssertion),
            IdentityComplete = identityComplete, CallsComplete = callsComplete };
    }

    private static bool KnownTarget(IMethodSymbol? method) => method is not null && method.MethodKind != MethodKind.DelegateInvoke;

    private static IEnumerable<IMethodSymbol> MethodGroups(SyntaxNode[] nodes, SourceAnalysisContext context)
    {
        foreach (var expression in nodes.OfType<ExpressionSyntax>().Where(node => node is SimpleNameSyntax or MemberAccessExpressionSyntax))
        {
            if (context.Model.GetTypeInfo(expression, context.Token).ConvertedType?.TypeKind != TypeKind.Delegate) continue;
            if (context.Model.GetSymbolInfo(expression, context.Token).Symbol is IMethodSymbol method) yield return method;
        }
    }

    private static bool Resolved(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => Resolved(array.ElementType),
        IPointerTypeSymbol pointer => Resolved(pointer.PointedAtType),
        INamedTypeSymbol named => named.TypeKind != TypeKind.Error && named.TypeArguments.All(Resolved),
        _ => type.TypeKind != TypeKind.Error
    };
}
