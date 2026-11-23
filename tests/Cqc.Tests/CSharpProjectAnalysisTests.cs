using Cqc.Core;
using Cqc.CSharp;
using Xunit;

namespace Cqc.Tests;

public sealed class CSharpProjectAnalysisTests
{
    [Fact]
    public async Task GenericAndNonGenericMethodsHaveSeparateMetricsFindingsAndCallTargets()
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Example.cs"), """
            class Example
            {
                static int F() => true ? 1 : 0;
                static int F<T>() => true ? (false ? 2 : 1) : 0;
                static int CallPlain() => F();
                static int CallGeneric() => F<int>();
            }
            """);

        var analyzer = new CSharpAnalyzer();
        var facts = await analyzer.AnalyzeAsync(await workspace.Discover(), CancellationToken.None);
        var plain = Assert.Single(facts.Methods, method => method.Id == "Example.F()");
        var generic = Assert.Single(facts.Methods, method => method.Id == "Example.F``1()");
        Assert.Equal(new[] { plain.Id }, Assert.Single(facts.Methods, method => method.Id == "Example.CallPlain()").Calls);
        Assert.Equal(new[] { generic.Id }, Assert.Single(facts.Methods, method => method.Id == "Example.CallGeneric()").Calls);

        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "quality.json"), """{"complexityThreshold":1}""");
        var result = await new AnalysisService([analyzer]).AnalyzeAsync(new()
        {
            ProjectRoot = workspace.Root,
            ConfigPath = "quality.json"
        }, ["code"], CancellationToken.None);
        var family = Assert.Single(result.Results);
        var entity = Assert.Single(family.Entities, item => item.Kind == "class");
        Assert.Equal(2, entity.Metrics["method:csharp:Example.F():complexity"]);
        Assert.Equal(3, entity.Metrics["method:csharp:Example.F``1():complexity"]);
        Assert.Equal(2, family.Findings.Count(finding => finding.Rule == "code.complexity"));
    }

    [Fact]
    public async Task IndependentProjectsKeepSameNamedTypesSeparateButMergeTheirOwnPartials()
    {
        using var workspace = FixtureWorkspace.Empty();
        await Project(workspace, "Alpha", "namespace Demo; public partial class Common { public int A() => 1; }");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Alpha", "Partial.cs"),
            "namespace Demo; public partial class Common { public int C() => 3; }");
        await Project(workspace, "Beta", "namespace Demo; public class Common { public int B() => 2; }");

        var analyzer = new CSharpAnalyzer();
        var facts = await analyzer.AnalyzeAsync(await workspace.Discover(), CancellationToken.None);
        var alphaId = "Alpha/Alpha.csproj#Demo.Common";
        var betaId = "Beta/Beta.csproj#Demo.Common";
        Assert.True(facts.SemanticComplete, string.Join("\n", facts.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(new[] { alphaId, alphaId, betaId }, facts.Types.Select(type => type.Id));
        Assert.All(facts.Types, type => Assert.Equal("Demo.Common", type.QualifiedName));
        var result = await new AnalysisService([analyzer]).AnalyzeAsync(new() { ProjectRoot = workspace.Root },
            ["code"], CancellationToken.None);
        Assert.Equal(2, Assert.Single(result.Results).Entities.Count(item => item.Kind == "class"));
    }

    [Fact]
    public async Task ReferencedProjectsResolveCallsAndTypesWhileMissingReferencesStayLocal()
    {
        using var workspace = FixtureWorkspace.Empty();
        await Project(workspace, "App", """
            namespace Demo;
            public class Consumer { public int Call() => new Library().Value(); }
            """, Reference("../Library/Library.csproj"));
        await Project(workspace, "Library", "namespace Demo; public class Library { public int Value() => 42; }");
        await Project(workspace, "Broken", "namespace Demo; public class Broken { public int Read() => 1; }",
            Reference("../Missing/Missing.csproj"));

        var analyzer = new CSharpAnalyzer();
        var facts = await analyzer.AnalyzeAsync(await workspace.Discover(), CancellationToken.None);
        Assert.False(facts.SemanticComplete);
        Assert.Contains(facts.Diagnostics, diagnostic => diagnostic.Code == "csharp.project_reference" && diagnostic.File == "Broken/Broken.csproj");
        var consumer = Assert.Single(facts.Methods, method => method.QualifiedName == "Demo.Consumer.Call()");
        var library = Assert.Single(facts.Methods, method => method.QualifiedName == "Demo.Library.Value()");
        Assert.Contains(library.Id, consumer.Calls);
        Assert.Contains(facts.Dependencies, edge => edge.From == consumer.TypeId && edge.To == library.TypeId);
        Assert.True(consumer.Evidence?.VerifiedContext);
        var broken = Assert.Single(facts.Methods, method => method.QualifiedName == "Demo.Broken.Read()");
        Assert.True(broken.Evidence?.Syntax);
        Assert.False(broken.Evidence?.VerifiedContext);
    }

    [Fact]
    public async Task ImplicitUsingsAndLanguageVersionAreScopedToTheirProject()
    {
        using var workspace = FixtureWorkspace.Empty();
        await Project(workspace, "Alpha", "class Alpha { List<int> Echo(List<int> values) => values; }",
            "<PropertyGroup><ImplicitUsings>enable</ImplicitUsings><LangVersion>12</LangVersion></PropertyGroup>");
        await Project(workspace, "Beta", "class Beta { List<int> Echo(List<int> values) => values; }",
            "<PropertyGroup><ImplicitUsings>disable</ImplicitUsings><LangVersion>14</LangVersion></PropertyGroup>");

        var facts = await new CSharpAnalyzer().AnalyzeAsync(await workspace.Discover(), CancellationToken.None);
        Assert.Equal(new[] { ("Alpha/Alpha.csproj", true), ("Beta/Beta.csproj", false) },
            facts.Contexts.Select(context => (context.ModulePath, context.Verified)));
        Assert.All(facts.Methods, method => Assert.True(method.Evidence?.Syntax));
        Assert.Contains(facts.Diagnostics, diagnostic => diagnostic.Code == "CS0246" && diagnostic.File == "Beta/Example.cs");
    }

    [Fact]
    public async Task CyclicProjectReferencesBlockBothContextsAndPreserveSyntaxFacts()
    {
        using var workspace = FixtureWorkspace.Empty();
        await Project(workspace, "Alpha", "class Alpha { int Read() => 1; }", Reference("../Beta/Beta.csproj"));
        await Project(workspace, "Beta", "class Beta { int Read() => 2; }", Reference("../Alpha/Alpha.csproj"));

        var facts = await new CSharpAnalyzer().AnalyzeAsync(await workspace.Discover(), CancellationToken.None);
        Assert.Equal(new[] { false, false }, facts.Contexts.Select(context => context.Verified));
        Assert.Equal(new bool?[] { true, true }, facts.Methods.Select(method => method.Evidence?.Syntax));
        var cycle = Assert.Single(facts.Diagnostics, diagnostic => diagnostic.Message.Contains("Cyclic", StringComparison.Ordinal));
        Assert.Equal("csharp.project_reference", cycle.Code);
    }

    [Fact]
    public async Task ConditionalProjectReferenceKeepsSyntaxEvidence()
    {
        using var workspace = FixtureWorkspace.Empty();
        await Project(workspace, "App", "class App { int Read() => 1; }",
            """<ItemGroup Condition="'$(Configuration)' == 'Debug'"><ProjectReference Include="../Lib/Lib.csproj" /></ItemGroup>""");
        await Project(workspace, "Lib", "class Lib { int Read() => 2; }");

        var facts = await new CSharpAnalyzer().AnalyzeAsync(await workspace.Discover(), CancellationToken.None);
        var app = Assert.Single(facts.Methods, method => method.QualifiedName == "App.Read()");
        Assert.Equal(((bool?)true, (bool?)false), (app.Evidence?.Syntax, app.Evidence?.VerifiedContext));
        Assert.Contains(facts.Diagnostics, diagnostic => diagnostic.Code == "csharp.project_reference" && diagnostic.File == "App/App.csproj");
    }

    [Fact]
    public async Task HostFrameworkFallbackCannotVerifyADifferentExplicitTarget()
    {
        using var workspace = FixtureWorkspace.Empty();
        await Project(workspace, "Legacy", "class Legacy { int Read() => 1; }",
            "<PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>");

        var facts = await new CSharpAnalyzer().AnalyzeAsync(await workspace.Discover(), CancellationToken.None);
        var method = Assert.Single(facts.Methods);
        Assert.Equal(((bool?)true, (bool?)false), (method.Evidence?.Syntax, method.Evidence?.VerifiedContext));
        Assert.Contains(facts.Diagnostics, diagnostic => diagnostic.Code == "csharp.framework_reference_unverified"
            && diagnostic.File == "Legacy/Legacy.csproj");
    }

    private static string Reference(string path) => $"""<ItemGroup><ProjectReference Include="{path}" /></ItemGroup>""";

    private static async Task Project(FixtureWorkspace workspace, string name, string source, string settings = "")
    {
        var directory = Directory.CreateDirectory(Path.Combine(workspace.Root, name)).FullName;
        await File.WriteAllTextAsync(Path.Combine(directory, name + ".csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              {settings}
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(directory, "Example.cs"), source);
    }
}
