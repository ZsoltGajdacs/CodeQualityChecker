using Cqc.Core;
using Xunit;

namespace Cqc.Tests;

public sealed class IndexedScoringTests
{
    private static readonly EvidenceState Verified = new(true, true, true, true, true, []);

    [Fact]
    public void ReachableCyclesMissingContextsAndLocalTypesKeepSeparateEvidence()
    {
        var types = new[] { Type("A", "a"), Type("B", "a"), Type("C", "b"), Type("Local", null), Type("Other", "other") };
        var facts = new AnalysisFacts(types, [], [], [], false)
        {
            Contexts = [new("a", "a/pom.xml", "main", ["b"], true, []),
                new("b", "b/pom.xml", "main", ["a", "missing"], true, ["custom.context"]),
                new("other", "other/pom.xml", "main", [], true, [])],
            SyntaxGaps = [new("Unparsed.java", "java", "b", "syntax.parse")]
        };
        var result = Evaluate(facts, "dependencies");
        foreach (var id in new[] { "A", "B", "C" })
        {
            var entity = Assert.Single(result.Entities, e => e.EntityId == id);
            Assert.Null(entity.Score);
            Assert.Equal(new[] { "context.unverified", "custom.context", "syntax.incomplete" }, entity.Blockers);
        }
        Assert.Equal(10, result.Entities.Single(e => e.EntityId == "Local").Score);
        Assert.Equal(10, result.Entities.Single(e => e.EntityId == "Other").Score);
        var refreshed = Evaluate(facts with { Contexts = facts.Contexts.Select(c => c with { ReachableContexts = [], Blockers = [] }).ToArray(), SyntaxGaps = [] }, "dependencies");
        Assert.All(refreshed.Entities, e => Assert.Equal(10, e.Score));
    }

    [Fact]
    public void SharedRecursiveHelperAndPartialFixturesContributeOncePerTest()
    {
        var types = new[] { Type("Tests", null) with { IsTest = true }, Type("Tests", null) with { Location = new("Part.java", 1, 20), IsTest = true },
            Type("Helper", null) with { Location = new("tests/Helper.java", 1, 20) } };
        var methods = new[] { Method("Tests.first", "Tests") with { IsTest = true, Calls = ["Helper.assert"] },
            Method("Tests.second", "Tests") with { IsTest = true, Calls = ["Helper.assert"], Location = new("Part.java", 2, 5) },
            Method("Tests.setup", "Tests") with { IsFixture = true, Calls = ["Helper.assert"] },
            Method("Helper.assert", "Helper") with { Assertions = 1, Calls = ["Helper.assert"], Location = new("tests/Helper.java", 2, 5) } };
        var result = Evaluate(new(types, methods, [], [], true), "tests");
        var entity = Assert.Single(result.Entities, e => e.EntityId == "Tests");
        Assert.Equal(10, entity.Score);
        Assert.Equal(1, entity.Metrics["method:Tests.first:assertionsWithHelpers"]);
        Assert.Equal(12, entity.Metrics["method:Tests.second:executableLinesWithHelpers"]);
        Assert.Empty(result.Findings);
        Assert.All(result.Entities.Where(e => e.Kind == "file"), e => Assert.Equal(10, e.Score));
    }

    private static TypeFact Type(string id, string? context) => new(id, "java", new(id + ".java", 1, 20), false)
        { ContextId = context, Evidence = Verified };
    private static MethodFact Method(string id, string type) => new(id, type, "java", new(type + ".java", 2, 5),
        1, 4, false, false, false, 0, [], [], 0) { Evidence = Verified };
    private static FamilyResult Evaluate(AnalysisFacts facts, string family)
    {
        var files = facts.Types.Select(t => t.Location.File).Distinct().ToArray();
        return Assert.Single(QualityEngine.Evaluate(new() { Facts = facts, Snapshot = new("/repo", files, files, [], "source", []),
            Families = [family], ConfigurationFingerprint = "config" }));
    }
}
