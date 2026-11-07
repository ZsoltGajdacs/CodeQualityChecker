using System.Text.Json;
using Cqc.Core;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Cqc.Tests;

public sealed class McpTests
{
    [Fact]
    public async Task StdioServerPublishesAndRunsAllSixTools()
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
            Assert.Equal(new[] { "check_all", "check_code_quality", "check_dependencies", "check_duplication", "check_test_quality", "inspect_project" }, tools.Select(t => t.Name).Order(StringComparer.Ordinal));
            foreach (var tool in tools)
            {
                Assert.Contains(Wire.Target, tool.Description);
                var result = await client.CallToolAsync(tool.Name,
                    new Dictionary<string, object?> { ["request"] = new { projectRoot = root } }, cancellationToken: timeout.Token);
                Assert.NotEqual(true, result.IsError);
                var text = string.Join("", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
                using var json = JsonDocument.Parse(text);
                Assert.Equal(Wire.Target, json.RootElement.GetProperty("message").GetString());
                if (tool.Name == "check_code_quality")
                    Assert.All(json.RootElement.GetProperty("results")[0].GetProperty("entities").EnumerateArray(), e => Assert.Equal(JsonValueKind.Null, e.GetProperty("score").ValueKind));
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
