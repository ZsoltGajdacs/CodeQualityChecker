using System.Text.Json;
using Cqc.Core;
using Cqc.CSharp;
using Cqc.Java;
using Xunit;

namespace Cqc.Tests;

public sealed class ScoringFixtureTests
{
    private static readonly string[] Families = ["code", "tests", "duplication", "dependencies"];

    [Fact]
    [Trait("Category", "Execution")]
    public async Task SourceGoldensPinScoresAcrossIndependentBuildsAndEvidenceReplay()
    {
        using var first = FixtureWorkspace.Copy("scoring");
        using var second = FixtureWorkspace.Copy("scoring");
        var expectations = JsonSerializer.Deserialize<ScoreWitness[]>(
            await File.ReadAllTextAsync(Path.Combine(McpTests.RepositoryRoot(), "tests", "Cqc.Tests", "Expectations", "scoring.expected.json")), Wire.Json)!;
        Assert.NotEmpty(expectations);
        var initial = await ExecuteAndVerify(first, expectations);
        var repeated = await ExecuteAndVerify(second, expectations);
        Assert.Equal(JsonSerializer.Serialize(initial, Wire.Json), JsonSerializer.Serialize(repeated, Wire.Json));
    }

    private static async Task<AnalysisResponse> ExecuteAndVerify(FixtureWorkspace workspace, ScoreWitness[] expectations)
    {
        var source = SourceBytes(workspace.Root);
        var service = new AnalysisService([new CSharpAnalyzer(), new JavaAnalyzer()]);
        var result = await service.AnalyzeAsync(new() { ProjectRoot = workspace.Root, Execute = true, TimeoutSeconds = 300 }, Families, CancellationToken.None);
        Assert.Empty(result.Diagnostics);
        Assert.All(result.Results, family => Assert.Equal("complete", family.Status));
        Assert.Equal(Wire.Target, result.Message);
        foreach (var witness in expectations) AssertWitness(result, witness);
        Assert.Equal(source, SourceBytes(workspace.Root));
        var replay = await service.AnalyzeAsync(new() { ProjectRoot = workspace.Root }, Families, CancellationToken.None);
        Assert.Equal(JsonSerializer.Serialize(result, Wire.Json), JsonSerializer.Serialize(replay, Wire.Json));
        Assert.Equal(source, SourceBytes(workspace.Root));
        return result;
    }

    private static void AssertWitness(AnalysisResponse response, ScoreWitness witness)
    {
        var family = Assert.Single(response.Results, f => f.Family == witness.Family);
        var entity = Assert.Single(family.Entities, e => e.Kind == "class" && e.EntityId == witness.Entity);
        Assert.Equal("complete", entity.Status);
        Assert.Equal(witness.Score, entity.Score);
        var findings = family.Findings.Where(f => entity.FindingIds.Contains(f.Id, StringComparer.Ordinal)).ToArray();
        Assert.Equal(witness.Rules.Order(StringComparer.Ordinal), findings.Select(f => f.Rule).Order(StringComparer.Ordinal));
        Assert.All(findings, finding => Assert.Equal(1, finding.Deduction));
        Assert.Equal(witness.Locations.OrderBy(l => l.StartLine), findings.SelectMany(f => f.Locations)
            .Where(l => l.File == witness.File).Distinct().OrderBy(l => l.StartLine));
        AssertFileScore(family, witness);
        AssertMetrics(entity, witness.Metrics);
        AssertGroupMembers(findings, witness.GroupMembers);
    }

    private static void AssertFileScore(FamilyResult family, ScoreWitness witness)
    {
        var file = Assert.Single(family.Entities, e => e.Kind == "file" && e.File == witness.File);
        Assert.Equal(witness.FileScore ?? witness.Score, file.Score);
        var rules = family.Findings.Where(f => file.FindingIds.Contains(f.Id, StringComparer.Ordinal)).Select(f => f.Rule);
        Assert.Equal((witness.FileRules ?? witness.Rules).Order(StringComparer.Ordinal), rules.Order(StringComparer.Ordinal));
    }

    private static void AssertMetrics(EntityScore entity, SortedDictionary<string, double> metrics)
    {
        foreach (var expected in metrics)
        {
            Assert.True(entity.Metrics.TryGetValue(expected.Key, out var actual), $"Missing metric {entity.EntityId}: {expected.Key}");
            Assert.Equal(expected.Value, actual);
        }
    }

    private static void AssertGroupMembers(Finding[] findings, string[]? expected)
    {
        if (expected is null) return;
        var group = Assert.Single(findings);
        Assert.Equal(expected.Order(StringComparer.Ordinal), group.EntityIds.Order(StringComparer.Ordinal));
        Assert.Equal(expected.Length, group.Locations.Length);
    }

    private static SortedDictionary<string, string> SourceBytes(string root) => new(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => !Path.GetRelativePath(root, path).StartsWith(".cqc/", StringComparison.Ordinal))
        .ToDictionary(path => Path.GetRelativePath(root, path), path => Wire.HashBytes(File.ReadAllBytes(path)), StringComparer.Ordinal), StringComparer.Ordinal);

    private sealed record ScoreWitness
    {
        public required string Family { get; init; }
        public required string Entity { get; init; }
        public required string File { get; init; }
        public required double Score { get; init; }
        public required string[] Rules { get; init; }
        public required Location[] Locations { get; init; }
        public SortedDictionary<string, double> Metrics { get; init; } = new(StringComparer.Ordinal);
        public double? FileScore { get; init; }
        public string[]? FileRules { get; init; }
        public string[]? GroupMembers { get; init; }
    }
}
