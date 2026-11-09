using Cqc.Core;
using Xunit;

namespace Cqc.Tests;

public sealed class ComplexityThresholdTests
{
    [Theory]
    [InlineData(8, 10, false)]
    [InlineData(9, 9, true)]
    [InlineData(10, 9, true)]
    [InlineData(11, 9, true)]
    public void DefaultBoundaryMatchesCodeHealth(int complexity, double expectedScore, bool expectedFinding)
    {
        var result = Evaluate(complexity);
        Assert.Equal(expectedScore, result.Entity.Score);
        Assert.Equal(expectedFinding, result.Family.Findings.Any(f => f.Rule == "code.complexity"));
        Assert.DoesNotContain(result.Family.Findings, f => f.Rule == "code.crap");
    }

    [Theory]
    [InlineData(9, 10)]
    [InlineData(10, 10)]
    [InlineData(11, 9)]
    public void ExplicitMaximumKeepsItsExistingMeaning(int complexity, double expectedScore)
    {
        var result = Evaluate(complexity, new() { ComplexityThreshold = 10 });
        Assert.Equal(expectedScore, result.Entity.Score);
    }

    [Fact]
    public void ComplexityNineIsReportedWithoutInventingCoverageOrScore()
    {
        var result = Evaluate(9, coverage: false);
        Assert.Equal("code.complexity", Assert.Single(result.Family.Findings).Rule);
        Assert.Equal(9, result.Entity.Metrics[$"method:{result.Method.Id}:complexity"]);
        Assert.Equal(new double?[] { null, null, null }, new[]
        {
            result.Entity.Score, result.Entity.Metrics[$"method:{result.Method.Id}:coverage"],
            result.Entity.Metrics[$"method:{result.Method.Id}:crap"]
        });
    }

    private sealed record Assessment(MethodFact Method, FamilyResult Family, EntityScore Entity);

    private static Assessment Evaluate(int complexity, QualityConfig? config = null, bool coverage = true)
    {
        var location = new Location("Boundary.cs", 2, 8);
        var method = new MethodFact("Boundary.evaluate()", "Boundary", "csharp", location, complexity, 4,
            false, false, false, 0, [], [], 20) { Evidence = new(true, true, true, true, true, []) };
        var facts = new AnalysisFacts([new("Boundary", "csharp", location, false) { Evidence = new(true, true, true, true, true, []) }], [method], [], [], true);
        var evidence = new CoverageEvidence("2", "source", "config", true, new(StringComparer.Ordinal), [new(method.Id, 2, 2, 4, 4)]);
        var response = QualityEngine.Evaluate(new()
        {
            Snapshot = new("/repo", [location.File], [location.File], [], "source", []), Facts = facts,
            Config = config ?? new(), Evidence = coverage ? evidence : null,
            ConfigurationFingerprint = "config", Families = ["code"]
        });
        var family = Assert.Single(response);
        return new(method, family, Assert.Single(family.Entities, e => e.Kind == "class"));
    }
}
