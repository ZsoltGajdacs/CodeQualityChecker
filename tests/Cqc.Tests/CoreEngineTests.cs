using Cqc.Core;
using Xunit;

namespace Cqc.Tests;

public sealed class CoreEngineTests
{
    [Fact]
    public void ProductionDeductionsRequireMatchingCoverageAndExplainEachMethod()
    {
        var method = Method("A.complex()", "A", new() { Complexity = 12 });
        var facts = Facts([method]);
        var report = Evidence([new(method.Id, 0, 2, 0, 4)]);
        var result = Evaluate(facts, "code", new() { Evidence = report });
        Assert.Equal(8, Class(result, "A").Score);
        Assert.Equal(new[] { "code.complexity", "code.crap" }, result.Findings.Select(f => f.Rule).Order());
        Assert.All(result.Findings, finding => Assert.Equal(method.Location, Assert.Single(finding.Locations)));
        var incomplete = Evaluate(facts, "code", new() { Evidence = report with { SourceFingerprint = "stale" } });
        Assert.Null(Class(incomplete, "A").Score);
        Assert.Equal("incomplete", incomplete.Status);
        Assert.Contains(incomplete.Findings, f => f.Rule == "code.complexity");
    }

    [Fact]
    public void BranchCoverageTakesPrecedenceAndBranchlessMethodsUseLines()
    {
        var branch = Method("A.branch()", "A", new() { Complexity = 4 });
        var straight = Method("A.straight()", "A");
        var result = Evaluate(Facts([branch, straight]), "code", new() { Evidence = Evidence([
            new(branch.Id, 1, 2, 10, 10), new(straight.Id, 0, 0, 2, 4)]) });
        Assert.Equal(.5, Class(result, "A").Metrics[$"method:{branch.Id}:coverage"]);
        Assert.Equal(.5, Class(result, "A").Metrics[$"method:{straight.Id}:coverage"]);
        Assert.Equal(6, Class(result, "A").Metrics[$"method:{branch.Id}:crap"]);
    }

    [Fact]
    public void MissingFailedDuplicateOrInvalidCoverageDoesNotBecomeZeroCoverage()
    {
        var method = Method("A.m()", "A");
        var facts = Facts([method]);
        var coverage = new MethodCoverage(method.Id, 1, 1, 4, 4);
        var reports = new CoverageEvidence?[] { null, Evidence([coverage]) with { Successful = false },
            Evidence([coverage, coverage]), Evidence([coverage with { CoveredBranches = 2 }]),
            Evidence([coverage]) with { ConfigurationFingerprint = "other" } };
        foreach (var report in reports)
        {
            var result = Evaluate(facts, "code", new() { Evidence = report });
            Assert.Null(Class(result, "A").Score);
            Assert.Null(Class(result, "A").Metrics[$"method:{method.Id}:coverage"]);
        }
    }

    [Fact]
    public void HelperAssertionsAndFixturesAreChargedOnceWithoutRecursiveExpansion()
    {
        var test = Method("Tests.test()", "Tests", new() { Test = true }) with { Calls = ["Tests.helper()"], IsParameterized = true };
        var helper = Method("Tests.helper()", "Tests", new() { Assertions = 1, Lines = 30 }) with { Calls = [test.Id] };
        var fixture = Method("Tests.setup()", "Tests", new() { Lines = 30 }) with { IsFixture = true };
        var result = Evaluate(Facts([test, helper, fixture]), "tests");
        Assert.Equal(9, Class(result, "Tests").Score);
        Assert.Equal("tests.size", Assert.Single(result.Findings).Rule);
        Assert.Equal(64, Class(result, "Tests").Metrics[$"method:{test.Id}:executableLinesWithHelpers"]);
        Assert.Equal(1, Class(result, "Tests").Metrics[$"method:{test.Id}:parameterized"]);
    }

    [Fact]
    public void EveryTestFindingDeductsAndScoresClampWithoutDiscardingFindings()
    {
        var tests = Enumerable.Range(0, 12).Select(i => Method($"Tests.test{i}()", "Tests", new() { Test = true })).ToArray();
        var result = Evaluate(Facts(tests), "tests");
        Assert.Equal(0, Class(result, "Tests").Score);
        Assert.Equal(12, result.Findings.Length);
        Assert.Equal(12, Class(result, "Tests").FindingIds.Length);
    }

    [Fact]
    public void UnresolvedTestHelperBlocksItsClassScore()
    {
        var test = Method("Tests.test()", "Tests", new() { Test = true }) with { Calls = ["Tests.helper()"] };
        var helper = Method("Tests.helper()", "Tests") with { Complete = false };
        var result = Evaluate(Facts([test, helper]), "tests");
        Assert.Null(Class(result, "Tests").Score);
        Assert.DoesNotContain(result.Findings, f => f.Rule == "tests.noAssertion");
    }

    [Fact]
    public void DuplicateGroupsUseFullContextAndDoNotCountEveryPair()
    {
        var methods = new[] { Method("A.m()", "A"), Method("B.m()", "B"), Method("C.m()", "C") };
        var result = Evaluate(Facts(methods), "duplication", new() { Selected = ["A.cs"] });
        Assert.Equal(9, Class(result, "A").Score);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(3, finding.Locations.Length);
        Assert.Equal(3, finding.EntityIds.Length);
        Assert.DoesNotContain(result.Entities, e => e.EntityId == "B");
    }

    [Fact]
    public void DuplicateCandidatesNeverCrossLanguages()
    {
        var left = Method("A.m()", "A");
        var right = Method("B.m()", "B") with { Language = "java" };
        var result = Evaluate(Facts([left, right]), "duplication");
        Assert.Empty(result.Findings);
        Assert.Equal(10, Class(result, "A").Score);
    }

    [Fact]
    public void CyclesAndBoundaryViolationsAreLocatedAgainstSelectedFiles()
    {
        var facts = Facts([Method("A.m()", "A"), Method("B.m()", "B")],
            [new("A", "B", new("A.cs", 3, 3)), new("B", "A", new("B.cs", 8, 8))]);
        var config = new QualityConfig
        {
            Components = new(StringComparer.Ordinal) { ["ui"] = ["A"], ["domain"] = ["B"] },
            AllowedDependencies = new(StringComparer.Ordinal) { ["ui"] = [] }
        };
        var result = Evaluate(facts, "dependencies", new() { Config = config, Selected = ["A.cs"] });
        Assert.Equal(8, Class(result, "A").Score);
        Assert.Equal(new[] { "dependencies.boundary", "dependencies.cycle" }, result.Findings.Select(f => f.Rule).Order());
        Assert.All(result.Findings, f => Assert.Equal("A.cs", Assert.Single(f.Locations).File));
        Assert.Equal(1, Class(result, "A").Metrics["componentFanIn"]);
        Assert.Equal(.5, Class(result, "A").Metrics["componentInstability"]);
    }

    [Fact]
    public void DiamondDependencyGraphIsNotACycleAndUnresolvedGraphCannotScore()
    {
        var methods = new[] { Method("A.m()", "A"), Method("B.m()", "B"), Method("C.m()", "C") };
        var facts = Facts(methods, [new("A", "B", new("A.cs", 1, 1)), new("A", "C", new("A.cs", 2, 2)),
            new("B", "C", new("B.cs", 1, 1))]);
        Assert.Empty(Evaluate(facts, "dependencies").Findings);
        var incomplete = Evaluate(facts with { SemanticComplete = false }, "dependencies");
        Assert.All(incomplete.Entities, e => Assert.Null(e.Score));
    }

    [Fact]
    public void PartialClassFileScoresUseOnlyFindingsInThatFileAndClassUsesAllParts()
    {
        var first = Method("Partial.a()", "Partial", new() { Test = true }) with { Location = new("First.cs", 2, 6) };
        var second = Method("Partial.b()", "Partial", new() { Test = true, Assertions = 1 }) with { Location = new("Second.cs", 2, 6) };
        var facts = new AnalysisFacts([new("Partial", "csharp", new("First.cs", 1, 7), false, true),
            new("Partial", "csharp", new("Second.cs", 1, 7), false, true)], [first, second], [], [], true);
        var result = Evaluate(facts, "tests");
        Assert.Equal(9, Class(result, "Partial").Score);
        Assert.Equal(9, Assert.Single(result.Entities, e => e.EntityId == "file:First.cs").Score);
        Assert.Equal(10, Assert.Single(result.Entities, e => e.EntityId == "file:Second.cs").Score);
    }

    [Fact]
    public void ResultsDoNotDependOnInputOrdering()
    {
        var methods = new[] { Method("A.m()", "A", new() { Test = true }), Method("B.m()", "B", new() { Test = true }) };
        var a = Evaluate(Facts(methods), "tests");
        var b = Evaluate(Facts(methods.Reverse().ToArray()), "tests");
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(a, Wire.Json), System.Text.Json.JsonSerializer.Serialize(b, Wire.Json));
    }

    [Fact]
    public void ComponentCyclesCanExistWithoutTypeCyclesAndPrefixlessPolicyMatchesQualifiedIds()
    {
        var methods = new[] { Method("csharp:Ui.A.m()", "csharp:Ui.A"), Method("csharp:Domain.B.m()", "csharp:Domain.B"),
            Method("csharp:Ui.C.m()", "csharp:Ui.C") };
        var edges = new[] { new DependencyFact(methods[0].TypeId, methods[1].TypeId, methods[0].Location),
            new DependencyFact(methods[1].TypeId, methods[2].TypeId, methods[1].Location) };
        var config = new QualityConfig { Components = new(StringComparer.Ordinal) { ["ui"] = ["Ui"], ["domain"] = ["Domain.*"] },
            ForbiddenDependencies = [["ui", "domain"]] };
        var result = Evaluate(Facts(methods, edges), "dependencies", new() { Config = config });
        Assert.Equal(8, Class(result, methods[0].TypeId).Score);
        Assert.Equal(9, Class(result, methods[1].TypeId).Score);
        Assert.Contains(result.Findings, f => f.Rule == "dependencies.cycle" && f.Message.Contains("component cycle"));
    }

    [Fact]
    public void SemanticFailureInOneLanguageDoesNotPreventOtherLanguageScoring()
    {
        var first = Method("A.m()", "A");
        var second = Method("B.m()", "B") with { Language = "java" };
        var facts = Facts([first, second]) with { SemanticComplete = false,
            Diagnostics = [new("unresolved", "Missing C# reference", Language: "csharp")] };
        var result = Evaluate(facts, "dependencies");
        Assert.Null(Class(result, "A").Score);
        Assert.Equal(10, Class(result, "B").Score);
    }

    [Fact]
    public void FailedAnalyzerCannotAppearAsSuccessfulEmptyAnalysis()
    {
        var facts = new AnalysisFacts([], [], [], [new("analyzer_failed", "Java worker failed", Language: "java")], false);
        var snapshot = new ProjectSnapshot("/repo", ["A.java"], ["A.java"], [], "source", []);
        var results = QualityEngine.Evaluate(new() { Snapshot = snapshot, Facts = facts, ConfigurationFingerprint = "config", Families = ["code", "tests", "duplication", "dependencies"] });
        Assert.All(results, result =>
        {
            Assert.Equal("incomplete", result.Status);
            Assert.Contains(result.Diagnostics, d => d.Code == "analyzer_failed");
            Assert.Empty(result.Entities);
        });
    }

    private sealed record MethodOptions
    {
        public int Complexity { get; init; } = 1;
        public bool Test { get; init; }
        public int Assertions { get; init; }
        public int Lines { get; init; } = 4;
    }

    private sealed record EvaluationOptions
    {
        public CoverageEvidence? Evidence { get; init; }
        public QualityConfig Config { get; init; } = new();
        public string[]? Selected { get; init; }
    }

    private static EntityScore Class(FamilyResult result, string id) => Assert.Single(result.Entities, e => e.Kind == "class" && e.EntityId == id);
    private static MethodFact Method(string id, string type, MethodOptions? options = null)
    {
        options ??= new();
        return new(id, type, "csharp", new(type + ".cs", 2, 8), options.Complexity, options.Lines,
            options.Test, false, false, options.Assertions, [], ["a", "b", "c"], 25);
    }
    private static AnalysisFacts Facts(MethodFact[] methods, DependencyFact[]? edges = null) => new(
        methods.Select(m => new TypeFact(m.TypeId, m.Language, new(m.Location.File, 1, 20), false, methods.Any(t => t.TypeId == m.TypeId && t.IsTest)))
            .DistinctBy(t => (t.Id, t.Location.File)).ToArray(), methods, edges ?? [], [], true);
    private static CoverageEvidence Evidence(MethodCoverage[] methods) => new("1", "source", "config", true, new(StringComparer.Ordinal), methods);
    private static FamilyResult Evaluate(AnalysisFacts facts, string family, EvaluationOptions? options = null)
    {
        options ??= new();
        var files = facts.Types.Select(t => t.Location.File).Distinct().Order(StringComparer.Ordinal).ToArray();
        var snapshot = new ProjectSnapshot("/repo", files, options.Selected ?? files, [], "source", []);
        return Assert.Single(QualityEngine.Evaluate(new() { Snapshot = snapshot, Facts = facts, Config = options.Config,
            Evidence = options.Evidence, ConfigurationFingerprint = "config", Families = [family] }));
    }
}
