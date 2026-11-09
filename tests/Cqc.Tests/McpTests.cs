using System.Text.Json;
using Cqc.Core;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Cqc.Tests;

public sealed class McpTests
{
    [Fact]
    public async Task StdioServerPublishesSummariesAndRetrievesAllSevenTools()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var root = Path.Combine(Path.GetTempPath(), "cqc-mcp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Example.cs"), "namespace Demo; public class Example { public int Sum(int a, int b) { return a + b; } }");
            var transport = Transport();
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
            var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
            Assert.Equal(new[] { "check_all", "check_code_quality", "check_dependencies", "check_duplication", "check_test_quality", "get_report_page", "inspect_project" }, tools.Select(t => t.Name).Order(StringComparer.Ordinal));
            foreach (var tool in tools.Where(t => t.Name != "get_report_page"))
            {
                Assert.Contains(Wire.Target, tool.Description);
                var result = await client.CallToolAsync(tool.Name,
                    new Dictionary<string, object?> { ["request"] = new { projectRoot = root } }, cancellationToken: timeout.Token);
                Assert.NotEqual(true, result.IsError);
                var json = JsonSerializer.SerializeToElement(result.StructuredContent);
                Assert.Equal("3", json.GetProperty("schemaVersion").GetString());
                Assert.Equal(Wire.Target, json.GetProperty("message").GetString());
                var raw = new System.Text.StringBuilder();
                string? cursor = null;
                do
                {
                    var pageResult = await client.CallToolAsync("get_report_page", new Dictionary<string, object?>
                    {
                        ["request"] = new { reportId = json.GetProperty("reportId").GetString(), section = "raw", cursor }
                    }, cancellationToken: timeout.Token);
                    Assert.NotEqual(true, pageResult.IsError);
                    var page = JsonSerializer.SerializeToElement(pageResult.StructuredContent);
                    raw.Append(page.GetProperty("rawChunk").GetString());
                    cursor = page.GetProperty("nextCursor").GetString();
                } while (cursor is not null);
                using var report = JsonDocument.Parse(raw.ToString());
                if (tool.Name == "check_code_quality")
                    Assert.All(report.RootElement.GetProperty("results")[0].GetProperty("entities").EnumerateArray(),
                        e => Assert.Equal(JsonValueKind.Null, e.GetProperty("score").ValueKind));

            }
            Assert.False(Directory.Exists(Path.Combine(root, ".cqc")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task InvalidScopeIsAnMcpToolError()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var transport = Transport();
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
        var result = await client.CallToolAsync("check_all", new Dictionary<string, object?>
        {
            ["request"] = new { projectRoot = Path.GetTempPath(), scope = "nonsense" }
        }, cancellationToken: timeout.Token);
        Assert.True(result.IsError);
    }

    private static StdioClientTransport Transport()
    {
        var root = RepositoryRoot();
        var executable = Environment.GetEnvironmentVariable("CQC_TEST_SERVER");
        var arguments = executable is null ? new[] { Path.Combine(root, "src", "Cqc.Server", "bin", "Debug", "net10.0", "cqc.dll") } : [];
        return new(new StdioClientTransportOptions
        {
            Name = "CQC test", Command = executable ?? "dotnet", Arguments = arguments,
            WorkingDirectory = root
        });
    }

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CodeQualityChecker.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
