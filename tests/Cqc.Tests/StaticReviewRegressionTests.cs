using System.Text.Json;
using Cqc.Core;
using Cqc.CSharp;
using Xunit;

namespace Cqc.Tests;

public sealed class StaticReviewRegressionTests
{
    [Theory]
    [InlineData("check_code_quality")]
    [InlineData("check_uncommitted_changes_quality")]
    public async Task PartialTypeScoresOnlyTheEvidenceInEachFile(string tool)
    {
        using var workspace = FixtureWorkspace.Empty();
        await ChangedScopeTests.Write(workspace, ".cqc.json", """{"complexityThreshold": 1}""");
        await ChangedScopeTests.Initialize(workspace);
        await ChangedScopeTests.Write(workspace, "Broken.cs", "partial class Example { int field = 1; }");
        await ChangedScopeTests.Write(workspace, "Healthy.cs", "partial class Example { int Value(bool flag) => flag ? 1 : 2; }");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpTests.Connect(timeout.Token);
        var summary = await McpTests.Read(client, tool, new { projectRoot = workspace.Root, execute = false }, timeout.Token);
        var report = await McpTests.Raw(client, McpTests.ReportId(summary), timeout.Token);
        var entities = report.GetProperty("results").EnumerateArray().Single(family => family.GetProperty("family").GetString() == "code")
            .GetProperty("entities").EnumerateArray().ToArray();
        var healthy = Assert.Single(entities, entity => entity.GetProperty("entityId").GetString() == "file:Healthy.cs");
        var complexity = healthy.GetProperty("staticComplexity");
        Assert.Equal(9, complexity.GetProperty("score").GetInt32());
        Assert.Single(complexity.GetProperty("findingIds").EnumerateArray());
        Assert.Contains("Healthy.cs", Assert.Single(healthy.GetProperty("metrics").EnumerateObject()).Name);
        Assert.All(entities.Where(entity => entity.GetProperty("entityId").GetString() != "file:Healthy.cs"),
            entity => Assert.Equal("incomplete", entity.GetProperty("staticComplexity").GetProperty("status").GetString()));
        Assert.Equal(3, entities.Length);
    }

    [Fact]
    public async Task SourceOnlyDiagnosticsReportEachConstructAndConfigurationReason()
    {
        using var workspace = FixtureWorkspace.Empty();
        await ChangedScopeTests.Write(workspace, "Constructs.cs", """
            class Example(int value)
            {
                int field = value;
                int Property { get; } = value;
            }
            """);
        Directory.CreateDirectory(Path.Combine(workspace.Root, "Configured"));
        await ChangedScopeTests.Write(workspace, "Configured/App.csproj", """<Project Sdk="Microsoft.NET.Sdk" />""");
        await ChangedScopeTests.Write(workspace, "Configured/App.cs", "class App { int Value() => 1; }");
        await ChangedScopeTests.Write(workspace, "Broken.java", "class Broken { void");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpTests.Connect(timeout.Token);
        var summary = await McpTests.Read(client, "check_code_quality", new { projectRoot = workspace.Root, execute = false }, timeout.Token);
        var page = await McpTests.Read(client, "get_report_page",
            new { reportId = McpTests.ReportId(summary), section = "diagnostics" }, timeout.Token);
        var diagnostics = page.GetProperty("items").EnumerateArray().ToArray();
        var constructs = diagnostics.Where(item => Matches(item, "Constructs.cs", "csharp.unmodeled_executable")).ToArray();
        Assert.Equal(new[] { 1, 3, 4 }, constructs.Select(item => item.GetProperty("location").GetProperty("startLine").GetInt32()));
        Assert.Equal(new[] { "primary constructor", "field initializer", "property initializer" },
            constructs.Select(item => item.GetProperty("construct").GetString()));
        Assert.Contains(diagnostics, item => Matches(item, "Configured/App.cs", "csharp.compile_configuration")
            && item.GetProperty("message").GetString()!.Contains("TargetFramework", StringComparison.Ordinal));
        var java = Assert.Single(diagnostics, item => Matches(item, "Broken.java", "syntax.parse"));
        Assert.Contains("Parse error", java.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SourceOnlyEngineEvaluatesOnlyRequestedFamilies()
    {
        using var workspace = FixtureWorkspace.Empty();
        await ChangedScopeTests.Write(workspace, "Example.cs", "class Example { int Value() => 1; }");
        var snapshot = (await workspace.Discover("repository")) with { SourceOnly = true };
        var facts = await new CSharpAnalyzer().AnalyzeAsync(snapshot, CancellationToken.None);
        var result = QualityEngine.Evaluate(new()
        {
            Snapshot = snapshot, Facts = facts, ConfigurationFingerprint = "", Families = ["duplication"]
        });

        Assert.Equal("duplication", Assert.Single(result).Family);
    }

    private static bool Matches(JsonElement item, string file, string code) =>
        item.TryGetProperty("file", out var path) && path.GetString() == file && item.GetProperty("code").GetString() == code;
}
