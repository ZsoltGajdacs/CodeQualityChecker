using Cqc.Core;
using Xunit;

namespace Cqc.Tests;

public sealed class AssessmentEvidenceTests
{
    [Fact]
    public void UnmodeledExecutableOnlyClassIsIncompleteRatherThanNotApplicable()
    {
        var facts = Facts(new(false, true, true, true, true, [])) with { Methods = [],
            SyntaxGaps = [new("A.java", "java", null, "syntax.unmodeledExecutable") { UnknownEntities = false }] };
        var result = Evaluate(facts, ["code"]).Single();
        Assert.Equal("incomplete", result.Status);
        var entity = result.Entities.Single(e => e.Kind == "class");
        Assert.Null(entity.Score);
        Assert.Null(entity.StaticComplexity!.Score);
        Assert.Contains("syntax.incomplete", entity.Blockers);
    }

    [Fact]
    public void AggregateLegacyCompletenessCannotReplaceExplicitEvidence()
    {
        var facts = Facts(new(true, true, true, true, true, []));
        facts = facts with { Types = [facts.Types.Single() with { Evidence = null }], Methods = [facts.Methods.Single() with { Evidence = null }], SemanticComplete = true };
        Assert.All(Evaluate(facts, ["code", "tests", "duplication", "dependencies"]).SelectMany(f => f.Entities), entity => Assert.Null(entity.Score));
    }

    [Fact]
    public void UnresolvedCallsPreserveComplexityAndStructuralScores()
    {
        var facts = Facts(new(true, true, false, false, false, ["java.unresolved_call"]));
        var results = Evaluate(facts, ["code", "duplication"]);
        var code = results.Single(r => r.Family == "code").Entities.Single(e => e.Kind == "class");
        Assert.Null(code.Score);
        Assert.Contains("coverage.missing", code.Blockers);
        Assert.Equal(9, code.StaticComplexity!.Score);
        Assert.Single(code.StaticComplexity.FindingIds);
        Assert.Equal(code.FindingIds, code.StaticComplexity.FindingIds);
        Assert.Equal(10, results.Single(r => r.Family == "duplication").Entities.Single(e => e.Kind == "class").Score);
    }

    [Fact]
    public void CoverageDoesNotRequireCallResolution()
    {
        var facts = Facts(new(true, true, false, false, true, []));
        var coverage = new CoverageEvidence("2", "source", "config", true, new(), [new("A.run()", 2, 2, 3, 3)]);
        var entity = Evaluate(facts, ["code"], coverage).Single().Entities.Single(e => e.Kind == "class");
        Assert.Equal(9, entity.Score);
        Assert.Empty(entity.Blockers);
    }

    [Fact]
    public void StructuralGapsPreventPerfectDuplicationScores()
    {
        var facts = Facts(new(true, true, true, true, true, [])) with
        { SyntaxGaps = [new("Missing.java", "java", "broken#main", "java.parse")] };
        Assert.All(Evaluate(facts, ["duplication"]).Single().Entities, entity =>
        {
            Assert.Null(entity.Score);
            Assert.Contains("structure.incomplete", entity.Blockers);
        });
    }

    [Fact]
    public void DependencyFailuresAreLimitedToReachableContexts()
    {
        var facts = Facts(new(true, true, true, true, true, []));
        var healthy = facts.Types.Single() with { ContextId = "healthy#main" };
        var broken = healthy with { Id = "Broken", ContextId = "broken#main", Location = new("Broken.java", 1, 5),
            Evidence = new(true, true, false, false, false, []) };
        facts = facts with { Types = [healthy, broken], SemanticComplete = false,
            Contexts = [new("healthy#main", "healthy/pom.xml", "main", [], true, []),
                new("broken#main", "broken/pom.xml", "main", [], false, ["context.unverified"])] };
        var result = Evaluate(facts, ["dependencies"]).Single();
        Assert.Equal(10, result.Entities.Single(e => e.EntityId == "A").Score);
        Assert.Null(result.Entities.Single(e => e.EntityId == "Broken").Score);
        facts = facts with { Contexts = [facts.Contexts[0] with { ReachableContexts = ["broken#main"] }, facts.Contexts[1]] };
        Assert.Null(Evaluate(facts, ["dependencies"]).Single().Entities.Single(e => e.EntityId == "A").Score);
    }

    [Fact]
    public void CoverageCannotEstablishCombinedScoreWithRejectedResolutionContext()
    {
        var facts = Facts(new(true, true, true, true, false, ["artifact.changed"]));
        var coverage = new CoverageEvidence("2", "source", "config", true, new(), [new("A.run()", 2, 2, 3, 3)]);
        var entity = Evaluate(facts, ["code"], coverage).Single().Entities.Single(e => e.Kind == "class");
        Assert.Null(entity.Score);
        Assert.Contains("context.unverified", entity.Blockers);
        Assert.Equal(9, entity.StaticComplexity!.Score);
    }

    [Fact]
    public void TestClosureIncludesHelperTypeStructuralBlockersOutsideSelectedScope()
    {
        var valid = new EvidenceState(true, true, true, true, true, []);
        var facts = Facts(valid);
        var test = facts.Methods.Single() with { IsTest = true, Calls = ["Helper.check()"], Assertions = 1 };
        var helper = test with { Id = "Helper.check()", TypeId = "Helper", IsTest = false, Calls = [], Location = new("src/test/Helper.java", 2, 5) };
        var helperType = new TypeFact("Helper", "java", new("src/test/Helper.java", 1, 8), false)
            { IsTest = true, Evidence = valid with { Syntax = false } };
        facts = facts with { Methods = [test, helper], Types = [facts.Types.Single() with { IsTest = true }, helperType] };
        var entity = Evaluate(facts, ["tests"]).Single().Entities.Single(e => e.Kind == "class");
        Assert.Null(entity.Score);
        Assert.Contains("syntax.incomplete", entity.Blockers);
    }

    [Fact]
    public void LegacyCoverageIsRejectedWithRefreshInstructions()
    {
        var coverage = new CoverageEvidence("1", "source", "config", true, new(), []);
        var error = Assert.Throws<ArgumentException>(() => EvidenceStore.Read(System.Text.Json.JsonSerializer.Serialize(coverage, Wire.Json)));
        Assert.Contains("Refresh", error.Message);
    }

    private static AnalysisFacts Facts(EvidenceState evidence)
    {
        var method = new MethodFact("A.run()", "A", "java", new("A.java", 2, 9), 9, 6,
            false, false, false, 0, [], ["body"], 25) { Evidence = evidence, DuplicationLines = 6 };
        var type = new TypeFact("A", "java", new("A.java", 1, 10), false) { Evidence = evidence };
        return new([type], [method], [], [], false);
    }

    private static FamilyResult[] Evaluate(AnalysisFacts facts, string[] families, CoverageEvidence? coverage = null) => QualityEngine.Evaluate(new()
    {
        Snapshot = new("/repo", ["A.java", "Broken.java"], ["A.java", "Broken.java"], [], "source", []),
        Facts = facts, Families = families, Evidence = coverage, ConfigurationFingerprint = "config"
    });
}
