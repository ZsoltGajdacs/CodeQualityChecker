using System.Text.Json;
using Xunit;

namespace Cqc.Tests;

public sealed class HotspotMcpTests
{
    [Fact]
    public async Task ReadsCommittedEditsAndWorkingTreeClassShape()
    {
        using var workspace = FixtureWorkspace.Empty();
        await ChangedScopeTests.Write(workspace, "Example.cs", "class Example { }");
        await ChangedScopeTests.Initialize(workspace);
        for (var edit = 1; edit <= 20; edit++)
        {
            await ChangedScopeTests.Write(workspace, "Example.cs", $"class Example {{ int Value() => {edit}; }}");
            await workspace.Git("add", ".");
            await workspace.Git("-c", "user.email=tests@example.invalid", "-c", "user.name=CQC Tests", "commit", "-qm", "edit");
        }
        var parameters = string.Join(", ", Enumerable.Range(1, 10).Select(index => $"int arg{index}"));
        await ChangedScopeTests.Write(workspace, "Example.cs", $"class Example({parameters})\n{{\n{new string('\n', 597)}}}\n");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var client = await McpTests.Connect(timeout.Token);
        var summary = await McpTests.Read(client, "check_hotspots", new { projectRoot = workspace.Root }, timeout.Token);
        Assert.False(summary.TryGetProperty("analysisNote", out _));
        var raw = await McpTests.Raw(client, McpTests.ReportId(summary), timeout.Token);
        var family = Assert.Single(raw.GetProperty("results").EnumerateArray());
        Assert.Equal("hotspots", family.GetProperty("family").GetString());
        Assert.Equal("complete", family.GetProperty("status").GetString());
        var entities = family.GetProperty("entities").EnumerateArray().ToArray();
        Assert.Equal(2, entities.Length);
        Assert.All(entities, entity => Assert.Equal(3.5, entity.GetProperty("score").GetDouble()));
        var metrics = Assert.Single(entities, entity => entity.GetProperty("kind").GetString() == "class").GetProperty("metrics");
        Assert.Equal(20, metrics.GetProperty("editCommits").GetInt32());
        Assert.Equal(10, metrics.GetProperty("constructorParameters").GetInt32());
        Assert.Equal(600, metrics.GetProperty("classLines").GetInt32());
        Assert.Single(family.GetProperty("findings").EnumerateArray());
        Assert.Equal(JsonValueKind.Object, raw.GetProperty("hotspotHistory").ValueKind);
    }

    [Fact]
    public async Task ReportsJavaAndTypeScriptClassesThroughTheMetricsPages()
    {
        using var workspace = FixtureWorkspace.Empty();
        await ChangedScopeTests.Write(workspace, "Example.java", """
            record Example(int a, int b, int c, int d, int e, int f) {
                Example { }
            }
            """);
        await ChangedScopeTests.Write(workspace, "Example.ts", """
            class Smaller {}
            class Example {
                constructor(a: number, b: number);
                constructor(a: number, b: number, c?: number, d?: number, e?: number, f?: number,
                            g?: number, h?: number, i?: number, j?: number) {}
            }
            """);
        await ChangedScopeTests.Initialize(workspace);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var client = await McpTests.Connect(timeout.Token);
        var summary = await McpTests.Read(client, "check_hotspots", new { projectRoot = workspace.Root }, timeout.Token);
        var reportId = McpTests.ReportId(summary);
        var page = await McpTests.Read(client, "get_report_page", new { reportId, section = "entities", family = "hotspots" }, timeout.Token);
        var entities = page.GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(entities, entity => entity.GetProperty("file").GetString() == "Example.java"
            && entity.GetProperty("kind").GetString() == "class" && entity.GetProperty("score").GetDouble() == 9);
        var typescript = entities.Where(entity => entity.GetProperty("file").GetString() == "Example.ts").ToArray();
        Assert.Equal(3, typescript.Length);
        var file = Assert.Single(typescript, entity => entity.GetProperty("kind").GetString() == "file");
        Assert.Equal(7, file.GetProperty("score").GetDouble());
        var largest = Assert.Single(typescript, entity => entity.GetProperty("kind").GetString() == "class"
            && entity.GetProperty("score").GetDouble() == 7);
        var entityId = largest.GetProperty("entityId").GetString();
        var metrics = await McpTests.Read(client, "get_report_page",
            new { reportId, section = "metrics", family = "hotspots", entityId }, timeout.Token);
        Assert.Contains(metrics.GetProperty("items").EnumerateArray(), metric =>
            metric.GetProperty("name").GetString() == "constructorParameters" && metric.GetProperty("value").GetInt32() == 10);
        var findings = await McpTests.Read(client, "get_report_page", new { reportId, section = "findings", family = "hotspots" }, timeout.Token);
        Assert.Equal(0, findings.GetProperty("total").GetInt32());
    }
}
