using Microsoft.CodeAnalysis;

namespace Cqc.CSharp;

internal static class TestRecognition
{
    public static bool IsTest(IMethodSymbol symbol) => HasAttribute(symbol, "Fact") || HasAttribute(symbol, "Theory");

    public static bool IsParameterized(IMethodSymbol symbol) => HasAttribute(symbol, "Theory");

    private static bool HasAttribute(IMethodSymbol symbol, string name) => symbol.GetAttributes().Any(x =>
        x.AttributeClass?.Name is var attributeName && (attributeName == name || attributeName == name + "Attribute") &&
        (x.AttributeClass?.ContainingNamespace.ToDisplayString() == "Xunit" || x.AttributeClass?.TypeKind == TypeKind.Error));

    public static bool IsTestType(INamedTypeSymbol symbol) => symbol.GetMembers().OfType<IMethodSymbol>().Any(IsTest) ||
        symbol.AllInterfaces.Any(x => x.ContainingNamespace.ToDisplayString() == "Xunit" &&
            x.Name is "IClassFixture" or "ICollectionFixture" or "IAsyncLifetime");

    public static bool IsFixture(IMethodSymbol symbol, bool testType) => testType &&
        (symbol.MethodKind == MethodKind.Constructor || symbol.Name is "Dispose" or "DisposeAsync" or "InitializeAsync");

    public static bool IsAssertion(IMethodSymbol? symbol)
    {
        if (symbol is null) return false;
        var type = symbol.ContainingType;
        var ns = type.ContainingNamespace.ToDisplayString();
        if (ns == "Xunit" && type.Name == "Assert") return true;
        if (ns.StartsWith("FluentAssertions", StringComparison.Ordinal) && symbol.Name != "Should") return true;
        return IsMockAssertion(ns, symbol.Name);
    }
    private static bool IsMockAssertion(string ns, string method)
    {
        var patterns = new[] { (Namespace: "Moq", Method: "Verify"), (Namespace: "NSubstitute", Method: "Received"),
            (Namespace: "FakeItEasy", Method: "MustHaveHappened") };
        return patterns.Any(x => ns.StartsWith(x.Namespace, StringComparison.Ordinal) && method.StartsWith(x.Method, StringComparison.Ordinal));
    }

}
