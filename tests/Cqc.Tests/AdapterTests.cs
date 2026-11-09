using Cqc.Core;
using Cqc.CSharp;
using Cqc.Java;
using Xunit;

namespace Cqc.Tests;

public sealed class AdapterTests
{
    [Fact]
    public async Task CSharpCannotResolveUndeclaredDependenciesFromTheAnalyzerHost()
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Example.cs"),
            "using Microsoft.CodeAnalysis; class Example { public SyntaxTree? Tree; }");
        var facts = await new CSharpAnalyzer().AnalyzeAsync(await workspace.Discover(), CancellationToken.None);
        Assert.False(facts.SemanticComplete);
        Assert.Contains(facts.Diagnostics, diagnostic => diagnostic.Code is "CS0234" or "CS0246");
        Assert.False(Assert.Single(facts.Types).Complete);
    }

    [Fact]
    public async Task CSharpRetainsFrameworkAssembliesOutsideSystemRuntime()
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Example.cs"),
            "using System.Data.Common; class Example { public DbConnection Echo(DbConnection connection) => connection; }");
        var facts = await new CSharpAnalyzer().AnalyzeAsync(await workspace.Discover(), CancellationToken.None);
        Assert.True(facts.SemanticComplete, string.Join("\n", facts.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(Assert.Single(facts.Methods).Complete);
    }

    [Fact]
    public async Task CSharpReadsRealXunitHelpersOverloadsPartialAndNestedTypes()
    {
        using var workspace = FixtureWorkspace.Copy("mixed/csharp");
        var snapshot = await workspace.Discover();
        await workspace.XunitReferences(snapshot);
        var facts = await new CSharpAnalyzer().AnalyzeAsync(snapshot, CancellationToken.None);
        Assert.True(facts.SemanticComplete, string.Join("\n", facts.Diagnostics.Select(x => x.Code + ": " + x.Message)));
        Assert.Equal(2, facts.Types.Count(x => x.Id == "Quality.Sample.Calculator"));
        Assert.Contains(facts.Types, x => x.Id == "Quality.Sample.Calculator.Nested");
        Assert.Contains(facts.Methods, x => x.Id == "Quality.Sample.Calculator.Choose(System.Int32)" && x.Complexity == 2);
        Assert.Contains(facts.Methods, x => x.Id == "Quality.Sample.Calculator.Choose(System.String)");
        Assert.Contains(facts.Methods, x => x.Id == "Quality.Sample.Calculator.get_Answer()");
        var test = Assert.Single(facts.Methods, x => x.IsParameterized);
        Assert.True(test.IsTest);
        var helper = Assert.Single(facts.Methods, x => x.Id.EndsWith(".Check(System.Int32)", StringComparison.Ordinal));
        Assert.Equal(1, helper.Assertions);
        Assert.Contains(helper.Id, test.Calls);
        Assert.DoesNotContain(facts.Types, x => x.Id.Contains("Generated", StringComparison.Ordinal));
        Assert.Contains(facts.Dependencies, x => x.From == "Quality.Sample.Left" && x.To == "Quality.Sample.Right");
        Assert.Contains(facts.Dependencies, x => x.From == "Quality.Sample.Right" && x.To == "Quality.Sample.Left");
    }

    [Fact]
    public async Task CSharpUnresolvedTypesCannotClaimSemanticCompleteness()
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Broken.cs"), "class Broken { Missing dependency; void Run() { dependency.Run(); } }");
        var facts = await new CSharpAnalyzer().AnalyzeAsync(await workspace.Discover(), CancellationToken.None);
        Assert.False(facts.SemanticComplete);
        Assert.Contains(facts.Diagnostics, x => x.Code == "CS0246");
        Assert.False(Assert.Single(facts.Methods).Complete);
    }

    [Fact]
    public async Task JavaProductionAdapterKeepsOverloadsNestedTypesAndCrossFileEdges()
    {
        using var workspace = FixtureWorkspace.Copy("mixed/java");
        var snapshot = await workspace.Discover();
        snapshot = snapshot with { Files = snapshot.Files.Where(x => x.StartsWith("src/main/", StringComparison.Ordinal)).ToArray() };
        var facts = await new JavaAnalyzer().AnalyzeAsync(snapshot, CancellationToken.None);
        Assert.NotEmpty(facts.Types);
        Assert.Contains(facts.Types, x => x.Id.Contains("Calculator.Nested", StringComparison.Ordinal));
        Assert.Equal(2, facts.Methods.Count(x => x.Id.Contains("Calculator.choose(", StringComparison.Ordinal)));
        Assert.Contains(facts.Methods, x => x.Id.Contains("Calculator.choose(int)", StringComparison.Ordinal) && x.Complexity == 2);
        Assert.Contains(facts.Dependencies, x => x.From == "pom.xml#main::demo.Left" && x.To == "pom.xml#main::demo.Right");
        Assert.Contains(facts.Dependencies, x => x.From == "pom.xml#main::demo.Right" && x.To == "pom.xml#main::demo.Left");
        Assert.False(facts.SemanticComplete);
        Assert.Contains(facts.Diagnostics, diagnostic => diagnostic.Code == "java.context_unverified");
        Assert.All(facts.Methods, method => Assert.True(method.Evidence!.Syntax));
    }
}
