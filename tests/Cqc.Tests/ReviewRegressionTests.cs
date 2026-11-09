using Cqc.Core;
using Cqc.CSharp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Cqc.Tests;

public sealed class ReviewRegressionTests
{
    [Theory]
    [InlineData("<PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup>")]
    [InlineData("<ItemGroup><PackageReference Include=\"xunit\" Version=\"2.9.3\" /></ItemGroup>")]
    public async Task CrossClassHelpersBelongToTestProjectEvenWithoutTestFolderNames(string marker)
    {
        using var workspace = FixtureWorkspace.Empty();
        Directory.CreateDirectory(Path.Combine(workspace.Root, "Checks", "Support"));
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Checks", "Checks.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>" + marker + "</Project>");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Checks", "Example.cs"),
            "using Xunit; namespace Demo; public class Example { [Fact] public void UsesHelper() => Support.Verify(3); }");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Checks", "Support", "Support.cs"),
            "using Xunit; namespace Demo; public static class Support { public static void Verify(int value) => Assert.Equal(3, value); }");
        await workspace.XunitReferences(await workspace.Discover());
        var result = await new AnalysisService([new CSharpAnalyzer()]).AnalyzeAsync(new()
            { ProjectRoot = workspace.Root }, ["code", "tests"], CancellationToken.None);
        Assert.Equal("notApplicable", Assert.Single(result.Results, family => family.Family == "code").Status);
        var tests = Assert.Single(result.Results, family => family.Family == "tests");
        Assert.Empty(tests.Findings);
        Assert.Equal(10, Assert.Single(tests.Entities, entity => entity.Kind == "class").Score);
    }

    [Fact]
    public async Task DebugSymbolsAndStaticProjectConstantsIncludeTheirRealBranches()
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Directory.Build.props"),
            "<Project><PropertyGroup><DefineConstants>$(DefineConstants);EXTRA</DefineConstants></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Sample.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>14</LangVersion></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Sample.cs"), """
            class Sample
            {
                public int Read(int value)
                {
            #if DEBUG
                    if (value > 0) return value;
            #endif
            #if EXTRA && NET10_0
                    if (value < 0) return -value;
            #endif
                    return 0;
                }
            }
            """);
        var facts = await new CSharpAnalyzer().AnalyzeAsync(await workspace.Discover(), CancellationToken.None);
        Assert.True(facts.SemanticComplete, string.Join("\n", facts.Diagnostics.Select(x => x.Message)));
        Assert.Equal(3, Assert.Single(facts.Methods).Complexity);
    }

    [Theory]
    [InlineData("<PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\"><DefineConstants>OTHER</DefineConstants></PropertyGroup>")]
    [InlineData("<ItemGroup><Compile Remove=\"Excluded.cs\" /></ItemGroup>")]
    [InlineData("<PropertyGroup><DefineConstants>$(CustomConstants)</DefineConstants></PropertyGroup>")]
    [InlineData("<PropertyGroup><Configuration>Release</Configuration></PropertyGroup>")]
    public async Task UnevaluatedCompileConfigurationCannotProduceVerifiedScores(string configuration)
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Sample.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>" + configuration + "</Project>");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Sample.cs"), "class Sample { public int Read() => 42; }");
        var result = await new AnalysisService([new CSharpAnalyzer()]).AnalyzeAsync(new()
            { ProjectRoot = workspace.Root }, ["duplication", "dependencies"], CancellationToken.None);
        Assert.Contains(result.Diagnostics, x => x.Code == "csharp.compile_configuration");
        Assert.All(result.Results.SelectMany(x => x.Entities), entity => Assert.Null(entity.Score));
    }

    [Fact]
    public async Task DuplicationWorkHonorsCancellationAfterAnalysisStarts()
    {
        var methods = Enumerable.Range(0, 2000).Select(index => new MethodFact($"Type{index}.Run()", $"Type{index}", "csharp",
            new($"Part{index}.cs", 1, 10), 1, 10, false, false, false, 0, [],
            Enumerable.Range(0, 256).Select(node => $"fingerprint-{index}-{node}").ToArray(), 300)).ToArray();
        var types = methods.Select(method => new TypeFact(method.TypeId, "csharp", method.Location, false)).ToArray();
        var files = types.Select(type => type.Location.File).ToArray();
        var snapshot = new ProjectSnapshot("/repo", files, files, [], "source", []);
        var facts = new AnalysisFacts(types, methods, [], [], true);
        using var cancellation = new CancellationTokenSource();
        var analysis = Task.Run(() =>
        {
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(25));
            return QualityEngine.Evaluate(new EvaluationRequest
            {
                Snapshot = snapshot, Facts = facts, ConfigurationFingerprint = "config",
                Families = ["duplication"], CancellationToken = cancellation.Token
            });
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analysis);
    }

    [Fact]
    [Trait("Category", "Execution")]
    public async Task McpExecutionTimeoutReturnsAnErrorWithoutScores()
    {
        using var workspace = FixtureWorkspace.Copy("mixed/csharp");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Quality.Sample.Tests", "SlowTests.cs"),
            "using Xunit; public class SlowTests { [Fact] public void Wait() => System.Threading.Thread.Sleep(30000); }");
        var before = workspace.SourceContents();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var server = Environment.GetEnvironmentVariable("CQC_TEST_SERVER");
        var transport = new StdioClientTransport(new()
        {
            Name = "CQC timeout regression", Command = server ?? "dotnet", WorkingDirectory = McpTests.RepositoryRoot(),
            Arguments = server is null ? [Path.Combine(McpTests.RepositoryRoot(), "src/Cqc.Server/bin/Debug/net10.0/cqc.dll")] : []
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: limit.Token);
        var result = await client.CallToolAsync("check_all", new Dictionary<string, object?>
            { ["request"] = new { projectRoot = workspace.Root, execute = true, timeoutSeconds = 1 } }, cancellationToken: limit.Token);
        Assert.True(result.IsError);
        var text = string.Join("", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        Assert.Contains("timed out", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"results\"", text, StringComparison.Ordinal);
        Assert.Equal(before, workspace.SourceContents());
    }

    [Fact]
    public async Task StaticImportedFieldsAndMethodGroupsRetainTheirDeclaringTypeDependency()
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "StaticFields.cs"), """
            using static Demo.StaticFields;
            namespace Demo;
            public static class StaticFields { public const int VALUE = 3; }
            public class FieldConsumer { private int value = VALUE; }
            """);
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "StaticMethods.cs"), """
            using System;
            using static Demo.StaticMethods;
            namespace Demo;
            public static class StaticMethods { public static void Run() { } }
            public class MethodConsumer { private Action action = Run; }
            """);
        var facts = await new CSharpAnalyzer().AnalyzeAsync(await workspace.Discover(), CancellationToken.None);
        Assert.True(facts.SemanticComplete);
        Assert.Contains(facts.Dependencies, edge => edge.From == "Demo.FieldConsumer" && edge.To == "Demo.StaticFields");
        Assert.Contains(facts.Dependencies, edge => edge.From == "Demo.MethodConsumer" && edge.To == "Demo.StaticMethods");
    }

    [Fact]
    public async Task ParseErrorWithoutMethodFactsCannotProducePerfectDuplicationScore()
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Broken.cs"), "class Broken { public int Field = ; }");
        var result = await new AnalysisService([new CSharpAnalyzer()]).AnalyzeAsync(new()
            { ProjectRoot = workspace.Root }, ["duplication", "dependencies"], CancellationToken.None);
        Assert.Contains(result.Diagnostics, x => x.Code.StartsWith("CS", StringComparison.Ordinal));
        Assert.All(result.Results, family => Assert.Equal("incomplete", family.Status));
        Assert.All(result.Results.SelectMany(family => family.Entities), entity => Assert.Null(entity.Score));
    }

    [Fact]
    public async Task TrackedArtifactsAreProtectedBeforeExecutionCanOverwriteThem()
    {
        using var workspace = FixtureWorkspace.Empty();
        await workspace.Git("init", "-q");
        var directory = Path.Combine(workspace.Root, ".cqc");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "coverage.json");
        await File.WriteAllTextAsync(file, "tracked-content");
        await workspace.Git("add", "-f", ".cqc/coverage.json");
        await Assert.ThrowsAsync<ArgumentException>(() => new AnalysisService([new CSharpAnalyzer()]).AnalyzeAsync(new()
            { ProjectRoot = workspace.Root, Execute = true }, ["code"], CancellationToken.None));
        Assert.Equal("tracked-content", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task MalformedImportedEvidenceProducesDiagnosticAndNoRiskScore()
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Sample.cs"), "class Sample { public int Read() => 42; }");
        var path = Path.Combine(workspace.Root, "invalid-coverage.json");
        await File.WriteAllTextAsync(path, "{\"methods\":[null],\"toolVersions\":{}}");
        var result = await new AnalysisService([new CSharpAnalyzer()]).AnalyzeAsync(new()
            { ProjectRoot = workspace.Root, ReportPath = path }, ["code"], CancellationToken.None);
        Assert.Contains(result.Diagnostics, x => x.Code == "coverage_invalid");
        Assert.All(Assert.Single(result.Results).Entities, entity => Assert.Null(entity.Score));
    }

    [Fact]
    public async Task SymbolicLinkArtifactDirectoryCannotRedirectExecutionWrites()
    {
        using var workspace = FixtureWorkspace.Empty();
        using var destination = FixtureWorkspace.Empty();
        Directory.CreateSymbolicLink(Path.Combine(workspace.Root, ".cqc"), destination.Root);
        await Assert.ThrowsAsync<ArgumentException>(() => new AnalysisService([new CSharpAnalyzer()]).AnalyzeAsync(new()
            { ProjectRoot = workspace.Root, Execute = true }, ["code"], CancellationToken.None));
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Root));
    }
}
