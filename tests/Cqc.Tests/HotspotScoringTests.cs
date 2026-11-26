using Cqc.Core;
using Xunit;

namespace Cqc.Tests;

public sealed class HotspotScoringTests
{
    [Theory]
    [InlineData(10, 4, 400, 10)]
    [InlineData(11, 4, 400, 9.75)]
    [InlineData(10, 7, 400, 10)]
    [InlineData(10, 8, 400, 9)]
    [InlineData(10, 10, 400, 7)]
    [InlineData(10, 4, 401, 10)]
    [InlineData(10, 4, 402, 9.99)]
    [InlineData(30, 10, 800, 0)]
    [InlineData(31, 11, 801, 0)]
    [InlineData(22, 4, 400, 7)]
    [InlineData(23, 4, 400, 6.75)]
    public void ScoresEachThresholdAndCapsAllThreePenalties(int edits, int parameters, int lines, double score)
    {
        const string file = "Example.cs";
        var end = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var family = Assert.Single(QualityEngine.Evaluate(new()
        {
            Snapshot = new(".", [file], [file], [], "source", []) { SourceOnly = true },
            Facts = new([], [], [], [], true) { ClassDeclarations = [new("csharp:Example", "csharp", new(file, 1, lines), parameters, true)] },
            ConfigurationFingerprint = "configuration",
            Families = ["hotspots"],
            HotspotEvidence = new(new("head", end.AddMonths(-12), end), new Dictionary<string, HashSet<string>>
            {
                [file] = Enumerable.Range(0, edits).Select(index => $"commit-{index}").ToHashSet(StringComparer.Ordinal)
            })
        }));

        Assert.Equal(2, family.Entities.Length);
        Assert.All(family.Entities, entity => Assert.Equal(score, entity.Score));
        Assert.Equal(score < 7 ? 1 : 0, family.Findings.Length);
    }
}
