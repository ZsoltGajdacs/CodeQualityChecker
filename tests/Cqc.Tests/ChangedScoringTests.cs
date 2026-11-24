using Cqc.Core;
using Xunit;

namespace Cqc.Tests;

public sealed class ChangedScoringTests
{
    private const string File = "Example.cs";
    private static readonly EvidenceState Complete = new(true, true, true, true, true, []);
    private static readonly Location Edited = new(File, 3, 3);
    private static readonly Location Untouched = new(File, 4, 4);

    [Fact]
    public async Task UntouchedMethodWithoutCoverageDoesNotBlockCombinedScore()
    {
        using var workspace = FixtureWorkspace.Empty();
        var request = await Evaluate(workspace);
        var coverage = new CoverageEvidence("2", request.Snapshot.SourceFingerprint, "config", true, new(), [new("edited", 2, 2, 3, 3)]);
        request = request with { Families = ["code"], Evidence = coverage };

        Assert.All(Assert.Single(QualityEngine.Evaluate(request)).Entities, entity =>
        {
            Assert.Equal(10, entity.Score);
            Assert.DoesNotContain(entity.Metrics.Keys, key => key.Contains("untouched", StringComparison.Ordinal));
        });
        Assert.All(Assert.Single(QualityEngine.Evaluate(request with { ChangedCode = null })).Entities, entity =>
        {
            Assert.Null(entity.Score);
            Assert.Contains("coverage.missing", entity.Blockers);
        });
    }

    [Fact]
    public async Task RepeatedDependencyUsesTouchedReferenceForCycleFinding()
    {
        using var workspace = FixtureWorkspace.Empty();
        var request = await Evaluate(workspace);
        var otherLocation = new Location("Other.cs", 1, 1);
        var facts = request.Facts with
        {
            Types = [..request.Facts.Types, new("B", "csharp", otherLocation, false) { Evidence = Complete }],
            Dependencies = [new("A", "B", Untouched), new("A", "B", Edited), new("B", "A", otherLocation)]
        };
        var scoped = Assert.Single(QualityEngine.Evaluate(request with { Facts = facts, Families = ["dependencies"] }));
        var finding = Assert.Single(scoped.Findings);
        Assert.Equal("dependencies.cycle", finding.Rule);
        Assert.Equal(Edited, Assert.Single(finding.Locations));
        Assert.All(scoped.Entities, entity => Assert.Empty(entity.Metrics));
    }

    private static async Task<EvaluationRequest> Evaluate(FixtureWorkspace workspace)
    {
        const string source = "class A\n{\n    int Edited() => 1;\n    int Untouched() => 2;\n}\n";
        await ChangedScopeTests.Write(workspace, File, source);
        await ChangedScopeTests.Initialize(workspace);
        await ChangedScopeTests.Write(workspace, File, source.Replace("=> 1;", "=> 3;", StringComparison.Ordinal));
        var snapshot = await workspace.Discover("changed");
        var facts = new AnalysisFacts([new("A", "csharp", new(File, 1, 5), false) { Evidence = Complete }],
            [Method("edited", Edited), Method("untouched", Untouched)], [], [], true);
        return new()
        {
            Snapshot = snapshot, Facts = facts, Families = [], ConfigurationFingerprint = "config",
            ChangedCode = await ChangedCodeScope.ReadAsync(snapshot, CancellationToken.None)
        };
    }

    private static MethodFact Method(string id, Location location) =>
        new(id, "A", "csharp", location, 2, 1, false, false, false, 0, [], [], 0) { Evidence = Complete };
}
