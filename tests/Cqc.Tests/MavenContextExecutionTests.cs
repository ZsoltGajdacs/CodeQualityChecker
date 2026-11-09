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
    public async Task ExecutionPublishesOrderedMainAndTestContextsWithReactorOwnership()
    {
        using var workspace = FixtureWorkspace.Copy("maven-reactor");
        var snapshot = await workspace.Discover();
        using var build = await new Cqc.Core.BuildExecutor([new JavaAnalyzer()]).CollectAsync(snapshot, CancellationToken.None);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(workspace.Root, ".cqc", "java-contexts.json")));
        var manifest = document.RootElement;
        Assert.Equal("2", manifest.GetProperty("schemaVersion").GetString());
        var contexts = manifest.GetProperty("contexts").EnumerateArray().ToArray();
        var main = Assert.Single(contexts, context => context.GetProperty("id").GetString() == "group/service/pom.xml#main");
        var test = Assert.Single(contexts, context => context.GetProperty("id").GetString() == "group/service/pom.xml#test");
        Assert.True(main.GetProperty("reachableContexts").EnumerateArray().Any(value => value.GetString() == "group/api/pom.xml#main"), main.ToString());
        Assert.Contains("group/api/pom.xml#test", main.GetProperty("reachableContexts").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("group/service/pom.xml#main", test.GetProperty("reachableContexts").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("group/api/pom.xml#test", test.GetProperty("reachableContexts").EnumerateArray().Select(value => value.GetString()));
        Assert.DoesNotContain(main.GetProperty("classpath").EnumerateArray(), ContainsJunit);
        Assert.Contains(test.GetProperty("classpath").EnumerateArray(), ContainsJunit);
    }
    [Fact]
    [Trait("Category", "Execution")]
    public async Task IndependentVersionsAndIdenticalSymbolsRetainOwnedCoverageAndIsolatedCacheFailures()
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
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(workspace.Root, ".cqc/java-contexts.json")));
        var red = Assert.Single(manifest.RootElement.GetProperty("contexts").EnumerateArray(), context => context.GetProperty("id").GetString() == "red/pom.xml#main");
        await File.WriteAllTextAsync(red.GetProperty("classpath")[0].GetProperty("path").GetString()!, "corrupted artifact");
        var altered = await service.AnalyzeAsync(request with { Execute = false }, ["code", "dependencies"], CancellationToken.None);
        var dependencies = Assert.Single(altered.Results, family => family.Family == "dependencies");
        var alteredCode = Assert.Single(altered.Results, family => family.Family == "code");
        var redCode = Assert.Single(alteredCode.Entities, entity => entity.EntityId == "java:red/pom.xml#main::sample.Shared");
        Assert.Null(redCode.Score);
        Assert.Equal(10, redCode.StaticComplexity!.Score);
        Assert.Equal(10, Assert.Single(alteredCode.Entities, entity => entity.EntityId == "java:blue/pom.xml#main::sample.Shared").Score);
        Assert.Null(Assert.Single(dependencies.Entities, entity => entity.EntityId == "java:red/pom.xml#main::sample.Shared").Score);
        Assert.Equal(10, Assert.Single(dependencies.Entities, entity => entity.EntityId == "java:blue/pom.xml#main::sample.Shared").Score);
        Assert.Contains(altered.Diagnostics, diagnostic => diagnostic.Code == "java.classpath_invalid");
        var repaired = await service.AnalyzeAsync(request, ["dependencies"], CancellationToken.None);
        Assert.DoesNotContain(repaired.Diagnostics, diagnostic => diagnostic.Code is "execution_failed" or "java.classpath_invalid");
        Assert.All(Assert.Single(repaired.Results).Entities, entity => Assert.NotNull(entity.Score));
    }

    private static bool ContainsJunit(JsonElement entry)
    {
        using var archive = ZipFile.OpenRead(entry.GetProperty("path").GetString()!);
        return archive.Entries.Any(item => item.FullName.StartsWith("org/junit/", StringComparison.Ordinal));
    }
}
