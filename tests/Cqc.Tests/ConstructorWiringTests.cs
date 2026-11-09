using Cqc.Core;
using Cqc.CSharp;
using Cqc.Java;
using Xunit;

namespace Cqc.Tests;

public sealed class ConstructorWiringTests
{
    [Theory]
    [InlineData("this.field = input;", true)]
    [InlineData("field = input;", true)]
    [InlineData("/* wiring */ this.field = input;", true)]
    [InlineData("this.field = input + 1;", false)]
    [InlineData("this.field = System.Math.Abs(input);", false)]
    [InlineData("this.field = (input);", false)]
    [InlineData("this.field = 1;", false)]
    [InlineData("this.field += input;", false)]
    [InlineData("this.Property = input;", false)]
    [InlineData("this.Shared = input;", false)]
    [InlineData("this.inherited = input;", false)]
    [InlineData("input = input;", false)]
    [InlineData("if (input > 0) this.field = input;", false)]
    [InlineData("this.field = input; System.Console.WriteLine(input);", false)]
    [InlineData("{ this.field = input; }", false)]
    [InlineData("this.field = input; ;", false)]
    [InlineData("", false)]
    public async Task ClassifiesOnlyDirectInstanceFieldWiring(string body, bool excluded)
    {
        var facts = await Analyze($$"""
            class Parent { protected int inherited; }
            class Sample : Parent {
                int field;
                static int Shared;
                int Property { get; set; }
                public Sample(int input) { {{body}} }
            }
            """);
        var constructor = Assert.Single(facts.Methods, method => method.Id.Contains(".ctor(", StringComparison.Ordinal));
        Assert.Equal(excluded, constructor.Fingerprints.Length == 0);
        Assert.True(constructor.Evidence!.Syntax);
    }

    [Theory]
    [InlineData("public Sample(int input) => this.field = input;", true)]
    [InlineData("public Sample(int input) : base() { this.field = input; }", false)]
    [InlineData("public Sample(int input) : this() { this.field = input; } public Sample() {}", false)]
    [InlineData("public Sample(int field) { field = field; }", false)]
    [InlineData("public Sample(int field) { this.field = field; }", true)]
    public async Task HandlesExpressionBodiesChainingAndShadowing(string constructors, bool excluded)
    {
        var facts = await Analyze($"class Sample {{ int field; {constructors} }}");
        var constructor = Assert.Single(facts.Methods, method => method.Id.Contains(".ctor(System.Int32)", StringComparison.Ordinal));
        Assert.Equal(excluded, constructor.Fingerprints.Length == 0);
    }

    [Fact]
    public async Task PartialFieldsWithUnknownDependencyTypesAreStillRecognized()
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Fields.cs"), "partial class Sample { Missing field; }");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Constructor.cs"),
            "partial class Sample { public Sample(Missing input) { field = input; } }");
        var facts = await new CSharpAnalyzer().AnalyzeAsync(await workspace.Discover(), CancellationToken.None);
        var constructor = Assert.Single(facts.Methods);
        Assert.Empty(constructor.Fingerprints);
        Assert.True(constructor.Evidence!.Syntax);
        Assert.False(facts.SemanticComplete);
        Assert.Equal(1, constructor.Complexity);
        Assert.Equal(1, constructor.ExecutableLines);
        Assert.True(constructor.NodeCount > 0);
    }

    [Theory]
    [InlineData("cs", "", false, true)]
    [InlineData("cs", "+ 1", true, true)]
    [InlineData("java", "", false, true)]
    [InlineData("java", "+ 1", true, true)]
    [InlineData("cs", "", true, false)]
    [InlineData("java", "", true, false)]
    public async Task DuplicationReportsExcludeOnlyWiringConstructors(string extension, string calculation, bool duplicated, bool constructor)
    {
        using var workspace = FixtureWorkspace.Empty();
        foreach (var type in new[] { "Left", "Right" })
        {
            var callable = constructor ? type : "void Assign";
            await File.WriteAllTextAsync(Path.Combine(workspace.Root, type + "." + extension), $$"""
                class {{type}} {
                    int a, b, c, d;
                    public {{callable}}(int first, int second, int third, int fourth) {
                        this.a = first {{calculation}};
                        this.b = second;
                        this.c = third;
                        this.d = fourth;
                    }
                }
                """);
        }
        ILanguageAnalyzer analyzer = extension == "cs" ? new CSharpAnalyzer() : new JavaAnalyzer();
        var report = await new AnalysisService([analyzer]).AnalyzeAsync(
            new() { ProjectRoot = workspace.Root }, ["duplication"], CancellationToken.None);
        var result = Assert.Single(report.Results);
        Assert.Equal(duplicated, result.Findings.Any());
        Assert.Equal(2, result.Entities.Count(entity => entity.Kind == "class"));
        Assert.All(result.Entities, entity => Assert.Equal(duplicated ? 9 : 10, entity.Score));
    }

    [Fact]
    public async Task OrdinaryAssignmentOnlyMethodsRemainEligible()
    {
        var facts = await Analyze("class Sample { int field; void Assign(int input) { this.field = input; } }");
        Assert.NotEmpty(Assert.Single(facts.Methods).Fingerprints);
    }

    private static async Task<AnalysisFacts> Analyze(string source)
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Sample.cs"), source);
        return await new CSharpAnalyzer().AnalyzeAsync(await workspace.Discover(), CancellationToken.None);
    }
}
