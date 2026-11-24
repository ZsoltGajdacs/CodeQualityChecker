using System.Text.Json;
using Xunit;

namespace Cqc.Tests;

public sealed class TouchedCodeMcpTests
{
    private const string ProjectTool = "check_code_quality";
    private const string ChangesTool = "check_uncommitted_changes_quality";
    private const string Example = "Example.cs";
    private const string Source = """
        class Example
        {
            int Edited(int n)
            {
                n += 1;
                return n > 0 ? 1 : 0;
            }
            int Untouched(int n) => n > 0 ? 9 : 0;
        }
        """;

    [Fact]
    public async Task ChangedMethodAloneContributesToStaticComplexity()
    {
        using var workspace = FixtureWorkspace.Empty();
        await Initialize(workspace, Source);
        await ChangedScopeTests.Write(workspace, Example, Source.Replace("? 1 :", "? 2 :", StringComparison.Ordinal));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpTests.Connect(timeout.Token);
        var request = new { projectRoot = workspace.Root };
        var changed = await ReadCode(ChangesTool);
        var finding = Assert.Single(changed.GetProperty("findings").EnumerateArray());
        Assert.Contains("Edited", finding.GetProperty("message").GetString());
        Assert.All(ExampleEntities(changed), entity =>
        {
            var assessment = entity.GetProperty("staticComplexity");
            Assert.Equal(9, assessment.GetProperty("score").GetInt32());
            Assert.False(assessment.TryGetProperty("blockers", out _));
            var prefix = entity.GetProperty("kind").GetString() == "file" ? "class:csharp:Example:" : "";
            var metric = Assert.Single(entity.GetProperty("metrics").EnumerateObject());
            Assert.Equal($"{prefix}method:csharp:Example.Edited(System.Int32)@Example.cs:20:complexity", metric.Name);
            Assert.Equal(2, metric.Value.GetInt32());
        });
        var summary = await McpTests.Read(client, ChangesTool, request, timeout.Token);
        var page = await McpTests.Read(client, "get_report_page", new { reportId = McpTests.ReportId(summary), section = "findings", family = "code" }, timeout.Token);
        Assert.Equal(finding.GetProperty("id").GetString(), Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal(2, (await ReadCode(ProjectTool)).GetProperty("findings").GetArrayLength());
        Assert.Equal(2, (await ReadCode("check_all", "changed")).GetProperty("findings").GetArrayLength());

        async Task<JsonElement> ReadCode(string tool, string? scope = null)
        {
            var arguments = scope is null ? (object)request : new { projectRoot = workspace.Root, scope };
            var summary = await McpTests.Read(client, tool, arguments, timeout.Token);
            return Code(await McpTests.Raw(client, McpTests.ReportId(summary), timeout.Token));
        }
    }

    [Fact]
    public async Task StagedChangeAndUnstagedDeletionUseCurrentWorkingTreeRanges()
    {
        using var workspace = FixtureWorkspace.Empty();
        await Initialize(workspace, Source);
        var staged = Source.Replace("? 9 :", "? 8 :", StringComparison.Ordinal);
        await ChangedScopeTests.Write(workspace, Example, staged);
        await workspace.Git("add", Example);
        var working = string.Join('\n', staged.Split('\n').Where(line => !line.Contains("n += 1", StringComparison.Ordinal)));
        await ChangedScopeTests.Write(workspace, Example, working);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpTests.Connect(timeout.Token);
        var summary = await McpTests.Read(client, ChangesTool, new { projectRoot = workspace.Root }, timeout.Token);
        var code = Code(await McpTests.Raw(client, McpTests.ReportId(summary), timeout.Token));
        Assert.Equal(2, code.GetProperty("findings").GetArrayLength());
        Assert.All(ExampleEntities(code), entity =>
            Assert.Equal(8, entity.GetProperty("staticComplexity").GetProperty("score").GetInt32()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoredStagedEditAndChangesBetweenMethodsHaveNoStaticScore(bool editBetweenMethods)
    {
        using var workspace = FixtureWorkspace.Empty();
        await Initialize(workspace, Source);
        await ChangedScopeTests.Write(workspace, Example, Source.Replace("? 9 :", "? 8 :", StringComparison.Ordinal));
        await workspace.Git("add", Example);
        var current = editBetweenMethods ? Source.Replace("    int Untouched", "    // Between methods\n    int Untouched", StringComparison.Ordinal) : Source;
        await ChangedScopeTests.Write(workspace, Example, current);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpTests.Connect(timeout.Token);
        var summary = await McpTests.Read(client, ChangesTool, new { projectRoot = workspace.Root }, timeout.Token);
        Assert.Equal(1, summary.GetProperty("fileCount").GetInt32());
        Assert.Empty(summary.GetProperty("families").EnumerateArray());
    }

    [Fact]
    public async Task NewFileIncludesAllMethods()
    {
        using var workspace = FixtureWorkspace.Empty();
        await Initialize(workspace, "class Existing {}");
        await ChangedScopeTests.Write(workspace, "New.cs", Source);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpTests.Connect(timeout.Token);
        var summary = await McpTests.Read(client, ChangesTool, new { projectRoot = workspace.Root }, timeout.Token);
        Assert.Equal(2, Code(await McpTests.Raw(client, McpTests.ReportId(summary), timeout.Token)).GetProperty("findings").GetArrayLength());
    }

    [Fact]
    public async Task PartialClassCloneOnlyDeductsFromTouchedMemberFile()
    {
        using var workspace = FixtureWorkspace.Empty();
        const string part = """
            partial class Example
            {
                int Clone(string value)
                {
                    string trimmed = value.Trim();
                    int length = trimmed.Length;
                    int doubled = length * 2;
                    return doubled + 1;
                }
                int Other() => 1;
            }
            """;
        var sibling = part.Replace("Clone(", "Counterpart(", StringComparison.Ordinal).Replace("Other(", "Helper(", StringComparison.Ordinal);
        await ChangedScopeTests.Write(workspace, "Left.cs", part);
        await ChangedScopeTests.Write(workspace, "Right.cs", sibling);
        await ChangedScopeTests.Initialize(workspace);
        await ChangedScopeTests.Write(workspace, "Left.cs", part.Replace("return doubled", "return /* Edited */ doubled", StringComparison.Ordinal));
        await ChangedScopeTests.Write(workspace, "Right.cs", sibling.Replace("=> 1;", "=> 2;", StringComparison.Ordinal));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpTests.Connect(timeout.Token);
        var summary = await McpTests.Read(client, ChangesTool, new { projectRoot = workspace.Root }, timeout.Token);
        var reportId = McpTests.ReportId(summary);
        var findings = await McpTests.Read(client, "get_report_page", new { reportId, section = "findings", family = "duplication" }, timeout.Token);
        var group = Assert.Single(findings.GetProperty("items").EnumerateArray());
        Assert.Equal(2, group.GetProperty("locations").GetArrayLength());
        var raw = await McpTests.Raw(client, reportId, timeout.Token);
        var duplication = Assert.Single(raw.GetProperty("results").EnumerateArray(), entry => entry.GetProperty("family").GetString() == "duplication");
        var files = duplication.GetProperty("entities").EnumerateArray().Where(entity => entity.GetProperty("kind").GetString() == "file");
        Assert.Equal(new[] { 9, 10 }, files.Select(file => file.GetProperty("score").GetInt32()));
        var counterpart = await McpTests.Read(client, "get_report_page", new
        { reportId, section = "findings", family = "duplication", entityId = "file:Right.cs" }, timeout.Token);
        Assert.Empty(counterpart.GetProperty("items").EnumerateArray());
    }

    private static async Task Initialize(FixtureWorkspace workspace, string source)
    {
        await ChangedScopeTests.Write(workspace, ".cqc.json", """{"complexityThreshold":1}""");
        await ChangedScopeTests.Write(workspace, Example, source);
        await ChangedScopeTests.Initialize(workspace);
    }

    private static JsonElement Code(JsonElement report) =>
        Assert.Single(report.GetProperty("results").EnumerateArray(), family => family.GetProperty("family").GetString() == "code");

    private static JsonElement[] ExampleEntities(JsonElement family)
    {
        var entities = family.GetProperty("entities").EnumerateArray().ToArray();
        Assert.Equal(new (string?, string?)[] { ("class", "csharp:Example"), ("file", $"file:{Example}") },
            entities.Select(entity => (entity.GetProperty("kind").GetString(), entity.GetProperty("entityId").GetString())));
        return entities;
    }
}
