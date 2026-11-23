using Cqc.Core;
using Cqc.CSharp;
using Xunit;

namespace Cqc.Tests;

public sealed class CSharpEvidenceTests
{
    [Fact]
    public async Task UnmodeledTopLevelProgramIsIncompleteRatherThanNotApplicable()
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Program.cs"), "System.Console.WriteLine(42);");
        var result = await new AnalysisService([new CSharpAnalyzer()]).AnalyzeAsync(new() { ProjectRoot = workspace.Root },
            ["code"], CancellationToken.None);
        Assert.Equal("incomplete", Assert.Single(result.Results).Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "csharp.unmodeled_executable");
    }

    [Fact]
    public async Task MissingSymbolsPreserveSyntaxMeasurementsAndStaticScores()
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Sample.cs"), """
            class Sample
            {
                public int Read(int value)
                {
                    MissingLibrary.Observe(value);
                    if (value > 0) return value;
                    return 0;
                }
            }
            """);
        var analyzer = new CSharpAnalyzer();
        var facts = await analyzer.AnalyzeAsync(await workspace.Discover(), CancellationToken.None);
        var method = Assert.Single(facts.Methods);
        Assert.NotNull(method.Evidence);
        Assert.True(method.Evidence.Syntax);
        Assert.False(method.Evidence.Calls);
        Assert.Equal(2, method.Complexity);
        Assert.NotEmpty(method.Fingerprints);

        var result = await new AnalysisService([analyzer]).AnalyzeAsync(new() { ProjectRoot = workspace.Root },
            ["code", "duplication", "dependencies"], CancellationToken.None);
        var code = Assert.Single(result.Results, family => family.Family == "code");
        var entity = Assert.Single(code.Entities, item => item.Kind == "class");
        Assert.Null(entity.Score);
        Assert.Equal(10, entity.StaticComplexity?.Score);
        Assert.Contains("coverage.missing", entity.Blockers);
        var duplicates = Assert.Single(result.Results, family => family.Family == "duplication");
        Assert.Equal(10, Assert.Single(duplicates.Entities, item => item.Kind == "class").Score);
        var dependencies = Assert.Single(result.Results, family => family.Family == "dependencies");
        Assert.Null(Assert.Single(dependencies.Entities, item => item.Kind == "class").Score);
    }

    [Fact]
    public async Task ParseErrorsRecordStructuralGapsAndBlockPerfectDuplication()
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Healthy.cs"), "class Healthy { int Read() => 42; }");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Broken.cs"), "class Broken { int Read( { return 0; }");
        var facts = await new CSharpAnalyzer().AnalyzeAsync(await workspace.Discover(), CancellationToken.None);
        Assert.Contains(facts.SyntaxGaps, gap => gap.File == "Broken.cs");
        var result = await new AnalysisService([new CSharpAnalyzer()]).AnalyzeAsync(new() { ProjectRoot = workspace.Root },
            ["duplication"], CancellationToken.None);
        Assert.All(Assert.Single(result.Results).Entities, entity => Assert.Null(entity.Score));
    }

    [Fact]
    public async Task UnmodeledInitializersBlockTypeStaticScoresWithoutLosingMethodMetrics()
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Sample.cs"), """
            class Sample
            {
                private int value = System.Environment.TickCount > 0 ? 1 : 2;
                public int Read() => value;
            }
            """);
        var facts = await new CSharpAnalyzer().AnalyzeAsync(await workspace.Discover(), CancellationToken.None);
        Assert.True(Assert.Single(facts.Methods).Evidence?.Syntax);
        Assert.False(Assert.Single(facts.Types).Evidence?.Syntax);
        Assert.Contains(facts.SyntaxGaps, gap => gap.Code == "csharp.unmodeled_executable");
        var result = await new AnalysisService([new CSharpAnalyzer()]).AnalyzeAsync(new() { ProjectRoot = workspace.Root },
            ["code"], CancellationToken.None);
        Assert.Null(Assert.Single(Assert.Single(result.Results).Entities, entity => entity.Kind == "class").StaticComplexity?.Score);
    }
    [Fact]
    public async Task DelegateMethodGroupsRetainHelpersAndIndirectCallsBlockCompleteScores()
    {
        using var workspace = FixtureWorkspace.Copy("mixed/csharp");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Quality.Sample.Tests", "DelegateCases.cs"), """
            using System;
            using Xunit;
            namespace Quality.Sample.Tests;
            public class DelegateCases
            {
                [Fact] public void Indirect()
                {
                    Action action = Branching;
                    action();
                    Assert.True(true);
                }
                [Fact] public void PassedToAssertion() => Assert.Throws<Exception>(Branching);
                [Fact] public void NameOnly() => Assert.Equal("Branching", nameof(Branching));
                private static void Branching()
                {
                    if (DateTime.UtcNow.Ticks > 0) throw new Exception();
                }
            }
            """);
        var snapshot = await workspace.Discover();
        await workspace.XunitReferences(snapshot);
        var analyzer = new CSharpAnalyzer();
        var facts = await analyzer.AnalyzeAsync(snapshot, CancellationToken.None);
        var helper = Assert.Single(facts.Methods, method => method.QualifiedName == "Quality.Sample.Tests.DelegateCases.Branching()");
        var indirect = Assert.Single(facts.Methods, method => method.QualifiedName == "Quality.Sample.Tests.DelegateCases.Indirect()");
        var passed = Assert.Single(facts.Methods, method => method.QualifiedName == "Quality.Sample.Tests.DelegateCases.PassedToAssertion()");
        var named = Assert.Single(facts.Methods, method => method.QualifiedName == "Quality.Sample.Tests.DelegateCases.NameOnly()");
        Assert.Contains(helper.Id, indirect.Calls);
        Assert.False(indirect.Evidence?.Calls);
        Assert.Contains(helper.Id, passed.Calls);
        Assert.True(passed.Evidence?.Calls);
        Assert.DoesNotContain(helper.Id, named.Calls);
        var result = await new AnalysisService([analyzer]).AnalyzeAsync(new() { ProjectRoot = workspace.Root },
            ["tests"], CancellationToken.None);
        var family = Assert.Single(result.Results);
        var entity = Assert.Single(family.Entities, item => item.EntityId == "csharp:" + indirect.TypeId);
        Assert.Null(entity.Score);
        Assert.Contains("tests.unresolvedCalls", entity.Blockers);
        Assert.Contains(family.Findings, finding => finding.Rule == "tests.branching"
            && finding.Message.Contains("DelegateCases.Indirect", StringComparison.Ordinal));
    }

}
