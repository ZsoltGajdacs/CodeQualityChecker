using Cqc.Core;
using Cqc.Java;
using Xunit;

namespace Cqc.Tests;

public sealed class MavenDownstreamCoverageTests
{
    [Fact]
    [Trait("Category", "Execution")]
    public async Task PublishesMergedAndDownstreamOnlyCoverageForTheProductionOwner()
    {
        using var workspace = FixtureWorkspace.Copy("maven-generated");
        var before = workspace.SourceContents();
        var service = new AnalysisService([new JavaAnalyzer()]);
        var request = new AnalysisRequest { ProjectRoot = workspace.Root, Execute = true };
        var report = await service.AnalyzeAsync(request, ["code"], CancellationToken.None);
        Assert.DoesNotContain(report.Diagnostics, diagnostic => diagnostic.Code is
            "execution_failed" or "java.coverage_aggregation_failed" or "java.coverage_missing");
        var coverage = EvidenceStore.Read(await workspace.ReadBundleEntryAsync("coverage.json"));

        var merged = Assert.Single(coverage.Methods, entry => entry.MethodId ==
            "java:api/pom.xml#main::sample.api.Decision.value(boolean)");
        Assert.Equal(2, merged.CoveredBranches);
        Assert.Equal(2, merged.TotalBranches);

        var downstreamOnly = Assert.Single(coverage.Methods, entry => entry.MethodId ==
            "java:upstream/pom.xml#main::sample.upstream.DownstreamOnly.measure(boolean)");
        Assert.Equal(1, downstreamOnly.CoveredBranches);
        Assert.Equal(2, downstreamOnly.TotalBranches);
        Assert.DoesNotContain(coverage.Methods, entry => entry.MethodId.Contains("sample.upstream.Unmeasured", StringComparison.Ordinal));

        var cached = await service.AnalyzeAsync(request with { Execute = false }, ["code"], CancellationToken.None);
        var type = Assert.Single(Assert.Single(cached.Results).Entities, entity =>
            entity.EntityId == "java:upstream/pom.xml#main::sample.upstream.DownstreamOnly");
        Assert.Equal(0.5, type.Metrics["method:java:upstream/pom.xml#main::sample.upstream.DownstreamOnly.measure(boolean):coverage"]);
        Assert.Equal(before, workspace.SourceContents());
    }
}
