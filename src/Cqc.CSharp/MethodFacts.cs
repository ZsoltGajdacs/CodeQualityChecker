using Cqc.Core;
using Microsoft.CodeAnalysis;

namespace Cqc.CSharp;

internal static class MethodFacts
{
    public static MethodFact Create(SyntaxNode declaration, IMethodSymbol symbol, SourceAnalysisContext context, bool testType)
    {
        var nodes = MethodDeclarations.BodyNodes(declaration).ToArray();
        var measurement = MethodMeasurement.Read(nodes);
        var fingerprints = StructuralFingerprint.Create(declaration, symbol, nodes);
        var resolution = context.SourceOnly ? MethodResolution.NotAttempted : MethodResolution.Read(declaration, nodes, symbol, context);
        var syntax = context.Options.Complete && !declaration.GetDiagnostics().Any(x => x.Severity == DiagnosticSeverity.Error);
        var evidence = new EvidenceState(syntax, resolution.IdentityComplete, resolution.CallsComplete,
            resolution.CallsComplete, context.VerifiedContext, []);
        var complete = syntax && resolution.IdentityComplete && resolution.CallsComplete && context.VerifiedContext;
        var name = ProjectSymbolIdentity.MethodName(symbol);
        var id = context.Identity.OwnMethod(symbol);
        if (context.SourceOnly)
            id += FormattableString.Invariant($"@{declaration.SyntaxTree.FilePath}:{declaration.SpanStart}");
        return new(id, context.Identity.OwnType(symbol.ContainingType), "csharp", SourceLocation.Of(declaration),
            measurement.Complexity, measurement.Lines, TestRecognition.IsTest(symbol), TestRecognition.IsFixture(symbol, testType),
            TestRecognition.IsParameterized(symbol), resolution.Assertions, resolution.Calls,
            fingerprints, measurement.NodeCount, complete, CoverageSignature: context.SourceOnly ? null : name)
        { ContextId = context.ContextId, QualifiedName = name, Evidence = evidence };
    }
}
