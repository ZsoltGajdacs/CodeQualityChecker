using Cqc.Core;
using Microsoft.CodeAnalysis;

namespace Cqc.CSharp;

internal static class MethodFacts
{
    public static MethodFact Create(SyntaxNode declaration, IMethodSymbol symbol, MethodAnalysisContext context)
    {
        var nodes = MethodDeclarations.BodyNodes(declaration).ToArray();
        var measurement = MethodMeasurement.Read(nodes);
        var resolution = MethodResolution.Read(declaration, nodes, symbol, context);
        var syntax = context.SyntaxConfiguration && !declaration.GetDiagnostics().Any(x => x.Severity == DiagnosticSeverity.Error);
        var evidence = new EvidenceState(syntax, resolution.IdentityComplete, resolution.CallsComplete,
            resolution.CallsComplete, context.VerifiedContext, []);
        var complete = syntax && resolution.IdentityComplete && resolution.CallsComplete && context.VerifiedContext;
        return new(SymbolNames.Method(symbol), SymbolNames.Type(symbol.ContainingType), "csharp", SourceLocation.Of(declaration),
            measurement.Complexity, measurement.Lines, TestRecognition.IsTest(symbol), TestRecognition.IsFixture(symbol, context.TestType),
            TestRecognition.IsParameterized(symbol), resolution.Assertions, resolution.Calls,
            measurement.Fingerprints, measurement.NodeCount, complete)
        { ContextId = SourceEvidence.ContextId, QualifiedName = SymbolNames.Method(symbol), Evidence = evidence };
    }
}

internal sealed record MethodAnalysisContext(SemanticModel Model, bool TestType, CancellationToken Token,
    bool SyntaxConfiguration, bool VerifiedContext);
