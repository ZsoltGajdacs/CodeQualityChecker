using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Cqc.Tests;

public sealed class ChangedScopeMcpTests
{
    [Fact]
    public async Task EveryInspectionAndCheckToolDescribesChangedScope()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await McpTests.Connect(timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        foreach (var tool in ScopedTools(tools))
        {
            var properties = tool.JsonSchema.GetProperty("properties").GetProperty("request").GetProperty("properties");
            var scope = properties.GetProperty("scope").GetProperty("description").GetString();
            Assert.Contains("repository", scope);
            Assert.Contains("paths", scope);
            Assert.Contains("changed", scope);
            Assert.Contains("staged", scope);
            Assert.Contains("unstaged", scope);
            Assert.Contains("untracked", scope);
            Assert.Contains("gitBase", scope);
            Assert.Contains("working", properties.GetProperty("gitBase").GetProperty("description").GetString());
        }
    }

    [Fact]
    public async Task EveryInspectionAndCheckToolPagesOnlyUncommittedFiles()
    {
        using var workspace = FixtureWorkspace.Empty();
        await ChangedScopeTests.Write(workspace, "Changed.cs", "class Changed {}");
        await ChangedScopeTests.Write(workspace, "Context.cs", "class Context {}");
        await ChangedScopeTests.Initialize(workspace);
        await ChangedScopeTests.Write(workspace, "Changed.cs", "class Changed { int Latest() => 2; }");
        await workspace.Git("add", "Changed.cs");
        await ChangedScopeTests.Write(workspace, "New.cs", "class New {}");
        var before = workspace.SourceContents();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpTests.Connect(timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        foreach (var tool in ScopedTools(tools))
        {
            var result = await McpTests.Call(client, tool.Name, new { projectRoot = workspace.Root, scope = "changed", execute = false }, timeout.Token);
            Assert.NotEqual(true, result.IsError);
            var summary = JsonSerializer.SerializeToElement(result.StructuredContent);
            Assert.Equal("changed", summary.GetProperty("scope").GetString());
            Assert.Equal(2, summary.GetProperty("fileCount").GetInt32());
            await AssertFilePages(client, summary.GetProperty("reportId").GetString()!, timeout.Token);
        }
        Assert.Equal(before, workspace.SourceContents());
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".cqc")));
    }

    [Fact]
    public async Task EveryInspectionAndCheckToolReportsNonGitScopeErrors()
    {
        using var workspace = FixtureWorkspace.Empty();
        await ChangedScopeTests.Write(workspace, "Example.cs", "class Example {}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await McpTests.Connect(timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        foreach (var tool in ScopedTools(tools))
        {
            var result = await McpTests.Call(client, tool.Name, new { projectRoot = workspace.Root, scope = "changed" }, timeout.Token);
            Assert.True(result.IsError);
            Assert.Contains("Git scope unavailable", Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
        }
    }

    private static async Task AssertFilePages(McpClient client, string reportId, CancellationToken token)
    {
        var first = await FilePage(client, reportId, null, token);
        Assert.Equal(2, first.GetProperty("total").GetInt32());
        Assert.Equal("Changed.cs", Assert.Single(first.GetProperty("items").EnumerateArray()).GetString());
        var cursor = first.GetProperty("nextCursor").GetString();
        Assert.NotNull(cursor);
        var second = await FilePage(client, reportId, cursor, token);
        Assert.Equal("New.cs", Assert.Single(second.GetProperty("items").EnumerateArray()).GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
    }

    private static async Task<JsonElement> FilePage(McpClient client, string reportId, string? cursor, CancellationToken token)
    {
        var result = await McpTests.Call(client, "get_report_page", new { reportId, section = "files", limit = 1, cursor }, token);
        Assert.NotEqual(true, result.IsError);
        return JsonSerializer.SerializeToElement(result.StructuredContent);
    }

    private static IEnumerable<McpClientTool> ScopedTools(IEnumerable<McpClientTool> tools) =>
        tools.Where(tool => tool.Name is "inspect_project" or "check_all" or "check_dependencies" or "check_duplication" or "check_test_quality");
}
