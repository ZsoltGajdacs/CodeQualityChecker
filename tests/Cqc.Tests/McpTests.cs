using System.Text;
using System.Text.Json;
using Cqc.Core;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Cqc.Tests;

public sealed class McpTests
{
    [Fact]
    public async Task StdioServerPublishesSummariesAndRetrievesAllNineTools()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var workspace = FixtureWorkspace.Empty();
        await ChangedScopeTests.Initialize(workspace);
        await ChangedScopeTests.Write(workspace, "Example.cs",
            "namespace Demo; public class Example { public int Sum(int a, int b) { return a + b; } }");
        await using var client = await Connect(timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(new[] { "check_all", "check_code_quality", "check_dependencies", "check_duplication", "check_hotspots", "check_test_quality",
            "check_uncommitted_changes_quality", "get_report_page", "inspect_project" }, tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        var pageRequest = Assert.Single(tools, tool => tool.Name == "get_report_page").JsonSchema
            .GetProperty("properties").GetProperty("request");
        Assert.True(pageRequest.GetProperty("properties").TryGetProperty("diagnosticCode", out _));
        Assert.DoesNotContain(pageRequest.GetProperty("required").EnumerateArray(), property => property.GetString() == "diagnosticCode");
        foreach (var tool in tools.Where(t => t.Name != "get_report_page"))
        {
            Assert.Contains(Wire.Target, tool.Description);
            var summary = await Read(client, tool.Name, new { projectRoot = workspace.Root }, timeout.Token);
            Assert.Equal("3", summary.GetProperty("schemaVersion").GetString());
            Assert.Equal(Wire.Target, summary.GetProperty("message").GetString());
            var report = await Raw(client, ReportId(summary), timeout.Token);
            Assert.Equal(Wire.Target, report.GetProperty("message").GetString());
        }
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".cqc")));
    }

    [Fact]
    public async Task InvalidScopeIsAnMcpToolError()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await Connect(timeout.Token);
        var result = await Call(client, "check_all", new { projectRoot = Path.GetTempPath(), scope = "nonsense" }, timeout.Token);
        Assert.True(result.IsError);
    }

    internal static Task<CallToolResult> Call(McpClient client, string tool, object request, CancellationToken token) =>
        client.CallToolAsync(tool, new Dictionary<string, object?> { ["request"] = request }, cancellationToken: token).AsTask();

    internal static async Task<JsonElement> Read(McpClient client, string tool, object request, CancellationToken token)
    {
        var result = await Call(client, tool, request, token);
        Assert.False(result.IsError == true, string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text)));
        return JsonSerializer.SerializeToElement(result.StructuredContent);
    }

    internal static string ReportId(JsonElement summary) => summary.GetProperty("reportId").GetString()!;

    internal static async Task<JsonElement> Raw(McpClient client, string reportId, CancellationToken token)
    {
        var raw = new StringBuilder();
        string? cursor = null;
        do
        {
            var page = await Read(client, "get_report_page", new { reportId, section = "raw", cursor }, token);
            raw.Append(page.GetProperty("rawChunk").GetString());
            cursor = page.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        using var report = JsonDocument.Parse(raw.ToString());
        return report.RootElement.Clone();
    }

    internal static Task<McpClient> Connect(CancellationToken token)
    {
        var root = RepositoryRoot();
        var executable = Environment.GetEnvironmentVariable("CQC_TEST_SERVER");
        return McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "CQC test", Command = executable ?? "dotnet", WorkingDirectory = root,
            Arguments = executable is null ? [Path.Combine(root, "src", "Cqc.Server", "bin", "Debug", "net10.0", "cqc.dll")] : []
        }), cancellationToken: token);
    }

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CodeQualityChecker.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
