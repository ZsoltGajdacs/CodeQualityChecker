using Cqc.Core;
using Cqc.CSharp;
using Cqc.Java;
using Xunit;

namespace Cqc.Tests;

public sealed class ComponentAssignmentTests
{
    [Theory]
    [InlineData("csharp")]
    [InlineData("java")]
    public async Task NestedTypesShareTheDeclaredComponentUnlessPolicyOverridesIt(string language)
    {
        using var workspace = FixtureWorkspace.Empty();
        await WriteNestedTypes(workspace, language);
        var defaultReport = await Dependencies(workspace);
        Assert.DoesNotContain(defaultReport.Findings, finding => finding.Rule == "dependencies.cycle");

        await ChangedScopeTests.Write(workspace, ".cqc.json", """
            { "components": { "nested": ["demo.Outer.Inner"] } }
            """);
        var configuredReport = await Dependencies(workspace);
        Assert.Equal(2, configuredReport.Findings.Count(finding => finding.Rule == "dependencies.cycle"
            && finding.Message.Contains("architectural component cycle", StringComparison.Ordinal)
            && finding.Message.Contains("nested", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task DifferentNamespacesCanFormAComponentCycleWithoutATypeCycle()
    {
        using var workspace = FixtureWorkspace.Empty();
        await ChangedScopeTests.Write(workspace, "Start.cs",
            "namespace Api; class Start { Data.Step Next() => new Data.Step(); }");
        await ChangedScopeTests.Write(workspace, "Step.cs",
            "namespace Data; class Step { Api.End Next() => new Api.End(); }");
        await ChangedScopeTests.Write(workspace, "End.cs", "namespace Api; class End { }");

        var report = await Dependencies(workspace);
        Assert.Equal(2, report.Findings.Count(finding => finding.Rule == "dependencies.cycle"
            && finding.Message.Contains("csharp:Api -> csharp:Data", StringComparison.Ordinal)));
    }

    private static async Task WriteNestedTypes(FixtureWorkspace workspace, string language)
    {
        if (language == "java")
        {
            Directory.CreateDirectory(Path.Combine(workspace.Root, "demo"));
            await ChangedScopeTests.Write(workspace, "demo/Outer.java", """
                package demo;
                public class Outer {
                    Inner next() { return new Inner(); }
                    static class Inner { Peer next() { return new Peer(); } }
                }
                """);
            await ChangedScopeTests.Write(workspace, "demo/Peer.java", "package demo; public class Peer { }");
            return;
        }

        await ChangedScopeTests.Write(workspace, "Outer.cs", """
            namespace demo;
            class Outer
            {
                Inner Next() => new Inner();
                class Inner { Peer Next() => new Peer(); }
            }
            """);
        await ChangedScopeTests.Write(workspace, "Peer.cs", "namespace demo; class Peer { }");
    }

    private static async Task<FamilyResult> Dependencies(FixtureWorkspace workspace)
    {
        var service = new AnalysisService([new CSharpAnalyzer(), new JavaAnalyzer()]);
        var result = await service.AnalyzeAsync(new() { ProjectRoot = workspace.Root }, ["dependencies"],
            CancellationToken.None);
        return Assert.Single(result.Results);
    }
}
