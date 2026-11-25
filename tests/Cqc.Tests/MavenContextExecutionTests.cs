using System.Text.Json;
using System.IO.Compression;
using Cqc.Java;
using Cqc.Core;
using Xunit;

namespace Cqc.Tests;

public sealed class MavenContextExecutionTests
{
    [Fact]
    [Trait("Category", "Execution")]
    public async Task ExecutionResolvesGeneratedMembersThroughMavenOutputs()
    {
        using var workspace = FixtureWorkspace.Copy("maven-generated");
        var service = new AnalysisService([new JavaAnalyzer()]);
        var result = await service.AnalyzeAsync(
            new() { ProjectRoot = workspace.Root, Execute = true }, ["dependencies", "tests"], CancellationToken.None);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "execution_failed" ||
            diagnostic.Code.StartsWith("java.unresolved_", StringComparison.Ordinal));
        var dependencies = Assert.Single(result.Results, family => family.Family == "dependencies");
        Assert.NotNull(Assert.Single(dependencies.Entities, entity =>
            entity.EntityId == "java:service/pom.xml#main::sample.service.DueDates").Score);
        var tests = Assert.Single(result.Results, family => family.Family == "tests");
        var test = Assert.Single(tests.Entities, entity =>
            entity.EntityId == "java:service/pom.xml#test::sample.service.DueDatesTest");
        Assert.NotNull(test.Score);
        Assert.Equal(2, test.Metrics["method:java:service/pom.xml#test::sample.service.DueDatesTest.readsGeneratedDate():assertionsWithHelpers"]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    public async Task ExecutionPublishesOrderedMainAndTestContextsWithReactorOwnership()
    {
        using var workspace = FixtureWorkspace.Copy("maven-reactor");
        var result = await new AnalysisService([new JavaAnalyzer()]).AnalyzeAsync(
            new() { ProjectRoot = workspace.Root, Execute = true, TimeoutSeconds = 300 }, ["dependencies"], CancellationToken.None);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "execution_failed");
        using var document = JsonDocument.Parse(await workspace.ReadBundleEntryAsync("java-contexts.json"));
        var manifest = document.RootElement;
        Assert.Equal("3", manifest.GetProperty("schemaVersion").GetString());
        var contexts = manifest.GetProperty("contexts").EnumerateArray().ToArray();
        var main = Assert.Single(contexts, context => context.GetProperty("id").GetString() == "group/service/pom.xml#main");
        var test = Assert.Single(contexts, context => context.GetProperty("id").GetString() == "group/service/pom.xml#test");
        Assert.True(main.GetProperty("reachableContexts").EnumerateArray().Any(value => value.GetString() == "group/api/pom.xml#main"), main.ToString());
        Assert.Contains("group/api/pom.xml#test", main.GetProperty("reachableContexts").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("group/service/pom.xml#main", test.GetProperty("reachableContexts").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("group/api/pom.xml#test", test.GetProperty("reachableContexts").EnumerateArray().Select(value => value.GetString()));
        Assert.DoesNotContain(main.GetProperty("classpath").EnumerateArray(), ContainsJunit);
        Assert.Contains(test.GetProperty("classpath").EnumerateArray(), ContainsJunit);
        Assert.Contains(test.GetProperty("classpath").EnumerateArray(), entry => entry.GetProperty("kind").GetString() == "directory");
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".cqc", "references")));
    }
    [Fact]
    [Trait("Category", "Execution")]
    public async Task IndependentVersionsRetainOwnedCoverageWhenOneModulesBuildOutputChanges()
    {
        using var workspace = FixtureWorkspace.Copy("maven-contexts");
        var before = workspace.SourceContents();
        var service = new AnalysisService([new JavaAnalyzer()]);
        var request = new AnalysisRequest { ProjectRoot = workspace.Root, Execute = true, TimeoutSeconds = 300 };
        var run = await service.AnalyzeAsync(request, ["code", "dependencies", "tests"], CancellationToken.None);
        Assert.DoesNotContain(run.Diagnostics, diagnostic => diagnostic.Code is "execution_failed" or "java.classpath_conflict");
        var code = Assert.Single(run.Results, family => family.Family == "code");
        foreach (var module in new[] { "red", "blue" })
        {
            var entity = Assert.Single(code.Entities, entity => entity.EntityId == "java:" + module + "/pom.xml#main::sample.Shared");
            Assert.Equal(10, entity.Score);
            Assert.Equal(1, entity.Metrics["method:java:" + module + "/pom.xml#main::sample.Shared.read():coverage"]);
        }
        var replay = await service.AnalyzeAsync(request with { Execute = false }, ["code", "dependencies", "tests"], CancellationToken.None);
        Assert.Equal(JsonSerializer.Serialize(run.Results, Wire.Json), JsonSerializer.Serialize(replay.Results, Wire.Json));
        Assert.Equal(before, workspace.SourceContents());
        Assert.DoesNotContain(code.Entities, entity => entity.EntityId.EndsWith("::sample.Generated", StringComparison.Ordinal));
        Assert.DoesNotContain(replay.Diagnostics, diagnostic => diagnostic.Code.StartsWith("java.unresolved_", StringComparison.Ordinal));
        var classFile = Path.Combine(workspace.Root, "red", "target", "classes", "sample", "Shared.class");
        var original = await File.ReadAllBytesAsync(classFile);
        await File.WriteAllTextAsync(classFile, "corrupted artifact");
        await AssertOnlyRedIsIncomplete(service, request);
        File.Delete(classFile);
        await AssertOnlyRedIsIncomplete(service, request);
        await File.WriteAllBytesAsync(classFile, original);
        var repaired = await service.AnalyzeAsync(request, ["dependencies"], CancellationToken.None);
        Assert.DoesNotContain(repaired.Diagnostics, diagnostic => diagnostic.Code is "execution_failed" or "java.classpath_invalid");
        Assert.All(Assert.Single(repaired.Results).Entities, entity => Assert.NotNull(entity.Score));
    }

    private static async Task AssertOnlyRedIsIncomplete(AnalysisService service, AnalysisRequest request)
    {
        var altered = await service.AnalyzeAsync(request with { Execute = false }, ["code", "dependencies"], CancellationToken.None);
        var dependencies = Assert.Single(altered.Results, family => family.Family == "dependencies");
        var code = Assert.Single(altered.Results, family => family.Family == "code");
        var red = Assert.Single(code.Entities, entity => entity.EntityId == "java:red/pom.xml#main::sample.Shared");
        Assert.Null(red.Score);
        Assert.Equal(10, red.StaticComplexity!.Score);
        Assert.Equal(10, Assert.Single(code.Entities, entity => entity.EntityId == "java:blue/pom.xml#main::sample.Shared").Score);
        Assert.Null(Assert.Single(dependencies.Entities, entity => entity.EntityId == "java:red/pom.xml#main::sample.Shared").Score);
        Assert.Equal(10, Assert.Single(dependencies.Entities, entity => entity.EntityId == "java:blue/pom.xml#main::sample.Shared").Score);
        Assert.Contains(altered.Diagnostics, diagnostic => diagnostic.Code == "java.classpath_invalid");
    }

    private static bool ContainsJunit(JsonElement entry)
    {
        if (entry.GetProperty("kind").GetString() != "jar") return false;
        using var archive = ZipFile.OpenRead(entry.GetProperty("path").GetString()!);
        return archive.Entries.Any(item => item.FullName.StartsWith("org/junit/", StringComparison.Ordinal));
    }
}
