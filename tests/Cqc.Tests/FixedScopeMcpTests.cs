using System.Text.Json;
using Cqc.Core;
using ModelContextProtocol.Client;
using Xunit;

namespace Cqc.Tests;

public sealed class FixedScopeMcpTests
{
    private const string ProjectTool = "check_code_quality";
    private const string ChangesTool = "check_uncommitted_changes_quality";
    private const string AnalysisNote = "Running with `execute: true` may reveal additional findings.";
    private const string ClonePath = "csharp/Scoring.Cases/CloneLeft.cs";

    [Theory]
    [InlineData(ProjectTool)]
    [InlineData(ChangesTool)]
    public async Task FixedScopeToolsExposeOnlyCommonRequestOptions(string tool)
    {
        using var workspace = FixtureWorkspace.Empty();
        await ChangedScopeTests.Initialize(workspace);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await McpTests.Connect(timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        var schema = Assert.Single(tools, candidate => candidate.Name == tool).JsonSchema;
        var properties = schema.GetProperty("properties").GetProperty("request").GetProperty("properties");
        Assert.Equal(new[] { "configPath", "execute", "projectRoot", "timeoutSeconds" },
            properties.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        foreach (var selector in new Dictionary<string, object> { ["scope"] = "paths", ["paths"] = new[] { "Example.cs" }, ["gitBase"] = "HEAD" })
        {
            var request = new Dictionary<string, object> { ["projectRoot"] = workspace.Root, [selector.Key] = selector.Value };
            Assert.True((await McpTests.Call(client, tool, request, timeout.Token)).IsError);
        }
    }

    [Fact]
    public async Task ChangesUseTheWorkingTreeAndRepositoryCloneContext()
    {
        using var workspace = FixtureWorkspace.Copy("scoring");
        await ChangedScopeTests.Initialize(workspace);
        await StageComplexityAndRestoreCloneInWorkingTree(workspace);
        await ChangedScopeTests.Write(workspace, "New.cs", "class New { int Value() => 1; }");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpTests.Connect(timeout.Token);
        var request = new { projectRoot = workspace.Root, execute = false };
        var changed = await McpTests.Read(client, ChangesTool, request, timeout.Token);
        var project = await McpTests.Read(client, ProjectTool, request, timeout.Token);
        Assert.Equal("changed", changed.GetProperty("scope").GetString());
        Assert.Equal("repository", project.GetProperty("scope").GetString());
        Assert.Equal(2, changed.GetProperty("fileCount").GetInt32());
        Assert.True(project.GetProperty("fileCount").GetInt32() > 2);
        await AssertChangedClone(client, changed, timeout.Token);
    }

    [Fact]
    public async Task CleanChangesSkipExecutionAndNonGitHasNoFallback()
    {
        using var workspace = FixtureWorkspace.Empty();
        await ChangedScopeTests.Write(workspace, "Example.cs", "class Example { int Value() => 1; }");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await McpTests.Connect(timeout.Token);
        var request = new { projectRoot = workspace.Root, execute = true };
        Assert.True((await McpTests.Call(client, ChangesTool, request, timeout.Token)).IsError);
        var source = await McpTests.Read(client, ProjectTool, new { projectRoot = workspace.Root }, timeout.Token);
        AssertSourceSummary(source);
        Assert.DoesNotContain(source.GetProperty("diagnostics").EnumerateArray(), diagnostic =>
            diagnostic.GetProperty("code").GetString() == "unsupported_integration");
        await ChangedScopeTests.Initialize(workspace);
        var summary = await McpTests.Read(client, ChangesTool, request, timeout.Token);
        Assert.Equal(0, summary.GetProperty("fileCount").GetInt32());
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".cqc")));
    }

    [Fact]
    public async Task SourceOnlyReportsKeepReliableResultsWhenOtherSyntaxIsMissing()
    {
        using var workspace = FixtureWorkspace.Copy("scoring");
        await ChangedScopeTests.Write(workspace, "Broken.cs", "class Broken { int Value( => 1; }");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpTests.Connect(timeout.Token);
        var summary = await McpTests.Read(client, ProjectTool, new { projectRoot = workspace.Root }, timeout.Token);
        var codeSummary = Family(summary, "families", "code");
        Assert.Equal("incomplete", codeSummary.GetProperty("status").GetString());
        Assert.Equal(1, codeSummary.GetProperty("unscoredFileCount").GetInt32());
        Assert.True(codeSummary.GetProperty("scoredFileCount").GetInt32() > 0);
        var reportId = McpTests.ReportId(summary);
        var raw = await McpTests.Raw(client, reportId, timeout.Token);
        var syntax = raw.GetProperty("diagnostics").EnumerateArray().First(diagnostic =>
            diagnostic.TryGetProperty("file", out var file) && file.GetString() == "Broken.cs");
        await AssertFilteredDiagnostic(client, reportId, syntax, timeout.Token);
        var code = Family(raw, "results", "code");
        var duplication = Family(raw, "results", "duplication");
        Assert.Equal("incomplete", duplication.GetProperty("status").GetString());
        var broken = code.GetProperty("entities").EnumerateArray().Where(entity => EntityFile(entity) == "Broken.cs").ToArray();
        Assert.Equal(2, broken.Length);
        Assert.All(broken, AssertIncompleteStaticEntity);
        var entities = duplication.GetProperty("entities").EnumerateArray().ToArray();
        Assert.Contains(entities, entity => EntityFile(entity)!.EndsWith(".java", StringComparison.Ordinal)
            && entity.GetProperty("score").ValueKind == JsonValueKind.Number);
        Assert.Contains(entities, entity => EntityFile(entity)!.EndsWith(".cs", StringComparison.Ordinal)
            && entity.GetProperty("status").GetString() == "incomplete" && !entity.TryGetProperty("score", out _));
        Assert.NotEmpty(duplication.GetProperty("findings").EnumerateArray());
        await AssertSourcePages(client, reportId, code, timeout.Token);
    }

    [Fact]
    [Trait("Category", "Execution")]
    public async Task ExplicitChangesExecutionAddsEvidenceWithoutChangingLaterSourceOnlyResults()
    {
        using var workspace = FixtureWorkspace.Copy("mixed");
        await ChangedScopeTests.Initialize(workspace);
        await MarkChanged(workspace, "csharp/Quality.Sample/Calculator.cs", "if (value > 0) return value + 1;");
        await MarkChanged(workspace, "java/src/main/java/demo/Calculator.java", "if (value > 0) return value + 1;");
        await MarkChanged(workspace, "csharp/Quality.Sample.Tests/CalculatorTests.cs", "calculator.Choose(0);");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var client = await McpTests.Connect(timeout.Token);
        var sourceRequest = new { projectRoot = workspace.Root };
        var before = await McpTests.Read(client, ChangesTool, sourceRequest, timeout.Token);
        var executed = await McpTests.Read(client, ChangesTool, new { projectRoot = workspace.Root, execute = true }, timeout.Token);
        Assert.Equal(new[] { "code", "dependencies", "duplication", "tests" }, Families(executed, "families"));
        Assert.Equal(3, executed.GetProperty("fileCount").GetInt32());
        Assert.False(executed.TryGetProperty("analysisNote", out var note) && note.ValueKind == JsonValueKind.String);
        Assert.True(File.Exists(Path.Combine(workspace.Root, ".cqc", EvidenceBundle.FileName)));
        Assert.Contains(Family(executed, "families", "code").GetProperty("scores").EnumerateArray(),
            bucket => bucket.GetProperty("score").ValueKind == JsonValueKind.Number);
        var after = await McpTests.Read(client, ChangesTool, sourceRequest, timeout.Token);
        var beforeRaw = await McpTests.Raw(client, McpTests.ReportId(before), timeout.Token);
        var afterRaw = await McpTests.Raw(client, McpTests.ReportId(after), timeout.Token);
        Assert.Equal(beforeRaw.GetRawText(), afterRaw.GetRawText());
    }

    private static async Task StageComplexityAndRestoreCloneInWorkingTree(FixtureWorkspace workspace)
    {
        var path = Path.Combine(workspace.Root, ClonePath);
        var original = await File.ReadAllTextAsync(path);
        var staged = (await File.ReadAllTextAsync(Path.Combine(workspace.Root, "csharp/Scoring.Cases/OverComplexity9.cs")))
            .Replace("OverComplexity9", "CloneLeft", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, staged);
        await workspace.Git("add", ClonePath);
        await File.WriteAllTextAsync(path, original.Replace("return doubled + 1;", "return doubled + 1; // Edited", StringComparison.Ordinal));
    }

    private static async Task AssertChangedClone(McpClient client, JsonElement summary, CancellationToken token)
    {
        var reportId = McpTests.ReportId(summary);
        var files = await McpTests.Read(client, "get_report_page", new { reportId, section = "files", limit = 1 }, token);
        Assert.Equal("New.cs", Assert.Single(files.GetProperty("items").EnumerateArray()).GetString());
        var cursor = files.GetProperty("nextCursor").GetString();
        Assert.NotNull(cursor);
        files = await McpTests.Read(client, "get_report_page", new { reportId, section = "files", limit = 1, cursor }, token);
        Assert.Equal(ClonePath, Assert.Single(files.GetProperty("items").EnumerateArray()).GetString());

        var raw = await McpTests.Raw(client, reportId, token);
        var code = Family(raw, "results", "code");
        Assert.Empty(code.GetProperty("findings").EnumerateArray());
        var group = Assert.Single(Family(raw, "results", "duplication").GetProperty("findings").EnumerateArray());
        Assert.Contains(group.GetProperty("locations").EnumerateArray(), location => EntityFile(location)!.EndsWith("/CloneRight.cs", StringComparison.Ordinal));
    }

    private static async Task AssertSourcePages(McpClient client, string reportId, JsonElement code, CancellationToken token)
    {
        var entities = code.GetProperty("entities").EnumerateArray().ToArray();
        Assert.NotEmpty(entities);
        Assert.All(entities, AssertStaticEntity);
        var page = await McpTests.Read(client, "get_report_page", new { reportId, section = "entities", family = "code", limit = 1 }, token);
        Assert.Equal(entities.Length, page.GetProperty("total").GetInt32());
        AssertStaticEntity(Assert.Single(page.GetProperty("items").EnumerateArray()));
        var metrics = await McpTests.Read(client, "get_report_page", new { reportId, section = "metrics", family = "code" }, token);
        Assert.NotEmpty(metrics.GetProperty("items").EnumerateArray());
        Assert.All(metrics.GetProperty("items").EnumerateArray(), item =>
        {
            Assert.EndsWith(":complexity", item.GetProperty("name").GetString());
            Assert.Equal(JsonValueKind.Number, item.GetProperty("value").ValueKind);
        });
    }

    private static async Task AssertFilteredDiagnostic(McpClient client, string reportId, JsonElement expected, CancellationToken token)
    {
        var diagnosticCode = expected.GetProperty("code").GetString();
        var page = await McpTests.Read(client, "get_report_page", new { reportId, section = "diagnostics", diagnosticCode }, token);
        var items = page.GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(items, item => EntityFile(item) == "Broken.cs");
        Assert.All(items, item => Assert.Equal(diagnosticCode, item.GetProperty("code").GetString()));
    }

    private static void AssertSourceSummary(JsonElement summary)
    {
        Assert.Equal(AnalysisNote, summary.GetProperty("analysisNote").GetString());
        Assert.Equal(new[] { "code", "duplication" }, Families(summary, "families"));
        var code = Family(summary, "families", "code");
        Assert.False(code.TryGetProperty("scores", out _));
        Assert.NotEmpty(code.GetProperty("staticComplexityScores").EnumerateArray());
        Assert.All(code.GetProperty("staticComplexityScores").EnumerateArray(),
            score => Assert.Equal(JsonValueKind.Number, score.GetProperty("score").ValueKind));
        Assert.Equal(code.GetProperty("entityCount").GetInt32(),
            code.GetProperty("entityStatusCounts").GetProperty("scored").GetInt32());
    }

    private static void AssertStaticEntity(JsonElement entity)
    {
        if (entity.GetProperty("status").GetString() == "incomplete")
        {
            AssertIncompleteStaticEntity(entity);
            return;
        }
        Assert.False(entity.TryGetProperty("score", out _));
        Assert.False(entity.TryGetProperty("blockers", out _));
        var assessment = entity.GetProperty("staticComplexity");
        Assert.Equal(JsonValueKind.Number, assessment.GetProperty("score").ValueKind);
        Assert.False(assessment.TryGetProperty("blockers", out _));
    }

    private static void AssertIncompleteStaticEntity(JsonElement entity)
    {
        var assessment = entity.GetProperty("staticComplexity");
        Assert.Equal("incomplete", assessment.GetProperty("status").GetString());
        Assert.False(assessment.TryGetProperty("score", out _));
        Assert.Contains(assessment.GetProperty("blockers").EnumerateArray(), blocker => blocker.GetString() == "syntax.incomplete");
    }

    private static string? EntityFile(JsonElement entity) => entity.GetProperty("file").GetString();

    private static JsonElement Family(JsonElement report, string section, string family) =>
        Assert.Single(report.GetProperty(section).EnumerateArray(), entry => entry.GetProperty("family").GetString() == family);

    private static IEnumerable<string?> Families(JsonElement report, string section) =>
        report.GetProperty(section).EnumerateArray().Select(entry => entry.GetProperty("family").GetString());

    private static async Task MarkChanged(FixtureWorkspace workspace, string file, string statement)
    {
        var path = Path.Combine(workspace.Root, file);
        var content = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, content.Replace(statement, statement.Replace(";", " /* Edited */;", StringComparison.Ordinal),
            StringComparison.Ordinal));
    }
}
