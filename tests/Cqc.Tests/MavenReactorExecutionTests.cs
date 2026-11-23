using System.Text.Json;
using Cqc.Core;
using Cqc.Java;
using Xunit;

namespace Cqc.Tests;

public sealed class MavenReactorExecutionTests
{
    [Fact]
    [Trait("Category", "Execution")]
    public async Task CancelledMavenExecutionDoesNotProduceCoverageEvidence()
    {
        using var workspace = FixtureWorkspace.Copy("maven-reactor");
        var test = Path.Combine(workspace.Root, "independent", "src", "test", "java", "independent", "OtherTest.java");
        await File.WriteAllTextAsync(test, (await File.ReadAllTextAsync(test)).Replace(
            "@Test void number() {", "@Test void number() throws Exception { Thread.sleep(30000);", StringComparison.Ordinal));
        var before = workspace.SourceContents();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var service = new AnalysisService([new JavaAnalyzer()]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AnalyzeAsync(
            new() { ProjectRoot = workspace.Root, Execute = true, TimeoutSeconds = 300 }, ["code"], cancellation.Token));
        Assert.False(File.Exists(Path.Combine(workspace.Root, ".cqc", "coverage.json")));
        Assert.Equal(before, workspace.SourceContents());
    }

    [Fact]
    [Trait("Category", "Execution")]
    public async Task FailedMavenTestDoesNotProduceCoverageEvidence()
    {
        using var workspace = FixtureWorkspace.Copy("maven-reactor");
        var test = Path.Combine(workspace.Root, "independent", "src", "test", "java", "independent", "OtherTest.java");
        await File.WriteAllTextAsync(test, (await File.ReadAllTextAsync(test)).Replace("assertEquals(7,", "assertEquals(99,", StringComparison.Ordinal));
        var before = workspace.SourceContents();
        var service = new AnalysisService([new JavaAnalyzer()]);
        var result = await service.AnalyzeAsync(new() { ProjectRoot = workspace.Root, Execute = true, TimeoutSeconds = 300 },
            ["code"], CancellationToken.None);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "execution_failed");
        Assert.All(Assert.Single(result.Results).Entities, entity => Assert.Null(entity.Score));
        Assert.False(File.Exists(Path.Combine(workspace.Root, ".cqc", "coverage.json")));
        Assert.Equal(before, workspace.SourceContents());
    }

    [Fact]
    [Trait("Category", "Execution")]
    public async Task NestedReactorAndIndependentBuildProduceReusableEvidenceWithoutChangingInputs()
    {
        using var workspace = FixtureWorkspace.Copy("maven-reactor");
        if (!OperatingSystem.IsWindows())
        {
            var wrapper = Path.Combine(workspace.Root, "mvnw");
            await File.WriteAllTextAsync(wrapper, "#!/bin/sh\nif [ \"$1\" = \"--version\" ]; then echo CQC_WRAPPER_SELECTED; fi\nexec mvn \"$@\"\n");
            File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var before = workspace.SourceContents();
        var snapshot = await workspace.Discover();
        Assert.Equal(new[] { "independent/pom.xml", "pom.xml" },
            MavenBuildRoots.Select(snapshot.Modules, workspace.Root).Select(module => module.Path).Order(StringComparer.Ordinal));

        var service = new AnalysisService([new JavaAnalyzer()]);
        var result = await service.AnalyzeAsync(new() { ProjectRoot = workspace.Root, Execute = true, TimeoutSeconds = 300 },
            ["code"], CancellationToken.None);
        var details = JsonSerializer.Serialize(result, Wire.Json);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "execution_failed");
        var code = Assert.Single(result.Results);
        foreach (var name in new[] { "example.api.Value", "example.service.Service", "independent.Other" })
        {
            var entity = Assert.Single(code.Entities, item => item.EntityId.EndsWith("::" + name, StringComparison.Ordinal));
            Assert.True(entity.Score.HasValue, details);
            Assert.Equal(1, entity.Metrics["method:" + entity.EntityId + ".number():coverage"]);
        }
        Assert.Equal(before, workspace.SourceContents());
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(workspace.Root, ".cqc", "java-contexts.json")));
        var classpath = manifest.RootElement.GetProperty("contexts").EnumerateArray()
            .SelectMany(context => context.GetProperty("classpath").EnumerateArray()).Select(entry => entry.GetProperty("path").GetString()!).ToArray();
        Assert.NotEmpty(classpath);
        Assert.All(classpath, path => Assert.True(File.Exists(path), path));
        var evidence = EvidenceStore.Read(await File.ReadAllTextAsync(Path.Combine(workspace.Root, ".cqc", "coverage.json")));
        Assert.Equal("0.8.15", evidence.ToolVersions["java:jacoco:group/api/pom.xml"]);
        Assert.Equal("0.8.15", evidence.ToolVersions["java:jacoco:group/service/pom.xml"]);
        Assert.Equal(JavaExecution.JacocoVersion, evidence.ToolVersions["java:jacoco:independent/pom.xml"]);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Contains("CQC_WRAPPER_SELECTED", evidence.ToolVersions["java:maven:pom.xml"], StringComparison.Ordinal);
            Assert.Contains("CQC_WRAPPER_SELECTED", evidence.ToolVersions["java:maven:independent/pom.xml"], StringComparison.Ordinal);
        }

        var replay = await service.AnalyzeAsync(new() { ProjectRoot = workspace.Root }, ["code"], CancellationToken.None);
        Assert.Equal(JsonSerializer.Serialize(code, Wire.Json), JsonSerializer.Serialize(Assert.Single(replay.Results), Wire.Json));
        Assert.DoesNotContain(replay.Diagnostics, diagnostic => diagnostic.Code is "java.stale_classpath" or "java.classpath");
        Assert.Equal(before, workspace.SourceContents());
    }

    [Fact]
    [Trait("Category", "Execution")]
    public async Task SkippedProjectAgentProducesModuleDiagnosticWithoutFallback()
    {
        using var workspace = FixtureWorkspace.Copy("maven-reactor");
        var pom = Path.Combine(workspace.Root, "group", "pom.xml");
        var xml = await File.ReadAllTextAsync(pom);
        await File.WriteAllTextAsync(pom, xml.Replace("<configuration><destFile>",
            "<configuration><skip>true</skip><destFile>", StringComparison.Ordinal));

        var service = new AnalysisService([new JavaAnalyzer()]);
        var result = await service.AnalyzeAsync(new() { ProjectRoot = workspace.Root, Execute = true, TimeoutSeconds = 300 },
            ["code"], CancellationToken.None);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "execution_failed");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "java.coverage_missing"
            && diagnostic.File == "group/api/pom.xml" && diagnostic.Message.Contains("skip", StringComparison.OrdinalIgnoreCase));
        var entities = Assert.Single(result.Results).Entities;
        Assert.Null(Assert.Single(entities, item => item.EntityId.EndsWith("::example.api.Value", StringComparison.Ordinal)).Score);
        Assert.NotNull(Assert.Single(entities, item => item.EntityId.EndsWith("::example.service.Service", StringComparison.Ordinal)).Score);
        Assert.NotNull(Assert.Single(entities, item => item.EntityId.EndsWith("::independent.Other", StringComparison.Ordinal)).Score);
    }
}
