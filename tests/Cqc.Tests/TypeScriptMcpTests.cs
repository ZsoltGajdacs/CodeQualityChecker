using System.Text.Json;
using Xunit;

namespace Cqc.Tests;

public sealed class TypeScriptMcpTests
{
    [Fact]
    public async Task ReviewsTsxAndResolvedImportsInRepositoryAndPathScopes()
    {
        using var workspace = FixtureWorkspace.Copy("typescript");
        var original = workspace.SourceContents();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpTests.Connect(timeout.Token);
        var request = new { projectRoot = workspace.Root };
        var structural = await Report("check_code_quality", request);
        var app = Assert.Single(Findings(structural, "code", "code.complexity"));
        Assert.Contains("complexity 9", app.GetProperty("message").GetString());
        Assert.Equal("src/ui/App.tsx", app.GetProperty("locations")[0].GetProperty("file").GetString());
        Assert.Single(Findings(structural, "duplication", "duplication.group"));

        var scoped = new { projectRoot = workspace.Root, scope = "paths", paths = new[] { "src/ui/App.tsx" } };
        var dependencies = await Report("check_dependencies", scoped);
        Assert.Equal("complete", dependencies.GetProperty("results")[0].GetProperty("status").GetString());
        Assert.Single(Findings(dependencies, "dependencies", "dependencies.cycle"));
        Assert.Single(Findings(dependencies, "dependencies", "dependencies.boundary"));
        Assert.Equal(dependencies.GetRawText(), (await Report("check_dependencies", scoped)).GetRawText());

        var executed = await Report("check_all", new { projectRoot = workspace.Root, execute = true });
        Assert.Contains(executed.GetProperty("diagnostics").EnumerateArray(),
            item => item.GetProperty("code").GetString() == "typescript.test_coverage_unavailable");
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".cqc")));
        Assert.Equal(original, workspace.SourceContents());

        async Task<JsonElement> Report(string tool, object arguments)
        {
            var summary = await McpTests.Read(client, tool, arguments, timeout.Token);
            return await McpTests.Raw(client, McpTests.ReportId(summary), timeout.Token);
        }
    }

    [Fact]
    public async Task UnresolvedImportsAndMalformedChangedFilesUseIndependentEvidence()
    {
        using var workspace = FixtureWorkspace.Copy("typescript");
        await ChangedScopeTests.Initialize(workspace);
        await ChangedScopeTests.Write(workspace, "src/state/broken.ts", "export const value = (;");
        await File.AppendAllTextAsync(Path.Combine(workspace.Root, "src/ui/App.tsx"), "\nimport 'missing-package';\n");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpTests.Connect(timeout.Token);
        var request = new { projectRoot = workspace.Root };
        var changed = await McpTests.Read(client, "check_uncommitted_changes_quality", request, timeout.Token);
        Assert.Equal(2, changed.GetProperty("fileCount").GetInt32());
        var structural = await McpTests.Raw(client, McpTests.ReportId(changed), timeout.Token);
        Assert.Empty(Findings(structural, "code", "code.complexity"));
        Assert.Equal(1, structural.GetProperty("results")[0].GetProperty("unscoredFileCount").GetInt32());
        var parserDiagnostic = Assert.Single(structural.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString() == "typescript.parse");
        Assert.StartsWith("TS", parserDiagnostic.GetProperty("message").GetString());
        Assert.Equal(1, parserDiagnostic.GetProperty("location").GetProperty("startLine").GetInt32());

        var architecture = await McpTests.Read(client, "check_dependencies", request, timeout.Token);
        var report = await McpTests.Raw(client, McpTests.ReportId(architecture), timeout.Token);
        Assert.Equal("incomplete", report.GetProperty("results")[0].GetProperty("status").GetString());
        Assert.NotEmpty(Findings(report, "dependencies", "dependencies.cycle"));
    }

    [Fact]
    public async Task EntryPointRequireCycleIsScoredWithAnAmbientImport()
    {
        using var workspace = FixtureWorkspace.Empty();
        await ChangedScopeTests.Write(workspace, "tsconfig.json",
            """{"files":["entry.ts","assets.d.ts"],"compilerOptions":{"traceResolution":true}}""");
        await ChangedScopeTests.Write(workspace, "entry.ts",
            """import "./entry.css"; export const peer = require("./peer");""");
        await ChangedScopeTests.Write(workspace, "peer.ts", """export const entry = require("./entry");""");
        await ChangedScopeTests.Write(workspace, "assets.d.ts", """declare module "*.css";""");
        await ChangedScopeTests.Write(workspace, "entry.css", ".entry { display: block; }");
        var original = workspace.SourceContents();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpTests.Connect(timeout.Token);
        var summary = await McpTests.Read(client, "check_dependencies", new { projectRoot = workspace.Root }, timeout.Token);
        var report = await McpTests.Raw(client, McpTests.ReportId(summary), timeout.Token);

        Assert.Equal("complete", report.GetProperty("results")[0].GetProperty("status").GetString());
        Assert.Equal(new[] { "entry.ts", "peer.ts" }, Findings(report, "dependencies", "dependencies.cycle")
            .Select(finding => finding.GetProperty("locations")[0].GetProperty("file").GetString())
            .Order(StringComparer.Ordinal));
        Assert.Equal(original, workspace.SourceContents());
    }

    private static IEnumerable<JsonElement> Findings(JsonElement report, string family, string rule) =>
        report.GetProperty("results").EnumerateArray().Single(result => result.GetProperty("family").GetString() == family)
            .GetProperty("findings").EnumerateArray().Where(finding => finding.GetProperty("rule").GetString() == rule);
}
