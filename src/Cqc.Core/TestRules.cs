namespace Cqc.Core;

internal static class TestRules
{
    private sealed record Assessment(MethodFact Test, int Lines, int Assertions, int Branches)
    {
        public string[] Blockers { get; init; } = [];
        public bool Complete => Blockers.Length == 0;
    }

    public static FamilyResult Evaluate(RuleContext context, QualityConfig config)
    {
        var tests = context.Methods.Where(m => m.IsTest).ToArray();
        if (tests.Length == 0) return context.Finish(new() { Family = "tests", Types = [], Findings = [] });
        var testTypes = context.Facts.Types.Where(t => t.IsTest).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        testTypes.UnionWith(tests.Select(t => t.TypeId));
        var lookup = context.Methods.Where(m => testTypes.Contains(m.TypeId) || IsTestPath(m.Location.File))
            .GroupBy(m => m.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var assessments = tests.Select(test =>
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return Assess(test, context, lookup);
        }).ToArray();
        var byType = assessments.ToLookup(a => a.Test.TypeId, StringComparer.Ordinal);
        var blockers = byType.ToDictionary(g => g.Key, g => g.SelectMany(a => a.Blockers)
            .Concat(context.Index.TypesById[g.Key].SelectMany(t => AssessmentEvidence.Syntax(t, []))).Distinct().ToArray(), StringComparer.Ordinal);
        var incomplete = assessments.Where(a => !a.Complete).Select(a => a.Test.TypeId).ToHashSet(StringComparer.Ordinal);
        var metrics = assessments.GroupBy(a => a.Test.TypeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, Metrics, StringComparer.Ordinal);
        var diagnostics = incomplete.Count == 0 ? Array.Empty<Diagnostic>() :
            [new Diagnostic("tests.incomplete", "Some test/fixture/helper facts are unresolved. Resolve diagnostics before scoring affected test classes.")];
        return context.Finish(new() { Family = "tests", Types = context.TypesFor(tests), Findings = assessments.SelectMany(a => Findings(a, config)),
            Blockers = id => blockers[id], Metrics = id => metrics[id], Diagnostics = diagnostics });
    }

    private static bool IsTestPath(string file) => file.Split('/').Any(part => part.Equals("test", StringComparison.OrdinalIgnoreCase)
        || part.Equals("tests", StringComparison.OrdinalIgnoreCase) || part.Equals("spec", StringComparison.OrdinalIgnoreCase) || part.EndsWith(".Tests", StringComparison.Ordinal));

    private static Assessment Assess(MethodFact test, RuleContext context, Dictionary<string, MethodFact> lookup)
    {
        var fixtures = context.Index.FixturesByType[test.TypeId];
        var closure = Helpers(test, fixtures, lookup);
        var types = closure.Select(m => m.TypeId).ToHashSet(StringComparer.Ordinal);
        return new(test, closure.Sum(m => m.ExecutableLines), closure.Sum(m => m.Assertions),
            closure.Sum(m => Math.Max(0, m.Complexity - 1))) { Blockers = closure.SelectMany(AssessmentEvidence.Test)
                .Concat(types.SelectMany(id => context.Index.TypesById[id])
                    .SelectMany(type => AssessmentEvidence.Syntax(type, []))).Distinct().Order(StringComparer.Ordinal).ToArray() };
    }

    private static IEnumerable<Finding> Findings(Assessment assessment, QualityConfig config)
    {
        if (assessment.Branches > 0)
            yield return ProductionRules.ForMethod("tests.branching", assessment.Test,
                "Test or its fixture/helper contains branching.", "Use explicit examples or parameterized cases to clarify the expected behavior.");
        if (assessment.Lines > config.TestLinesThreshold)
            yield return ProductionRules.ForMethod("tests.size", assessment.Test,
                $"Test plus fixture/helper contributions has {assessment.Lines} executable lines, exceeding {config.TestLinesThreshold}.",
                "Reduce setup and split independent behaviors; moving code into helpers does not remove its cost.");
        if (assessment.Assertions == 0 && assessment.Complete)
            yield return ProductionRules.ForMethod("tests.noAssertion", assessment.Test,
                "No recognized observable assertion was found, including resolved helpers.",
                "Inspect test intent and add an outcome assertion, or configure/implement recognition of the assertion abstraction.");
    }

    private static SortedDictionary<string, double?> Metrics(IEnumerable<Assessment> assessments)
    {
        var result = RuleContext.Metrics();
        foreach (var assessment in assessments)
        {
            result[$"method:{assessment.Test.Id}:executableLinesWithHelpers"] = assessment.Lines;
            result[$"method:{assessment.Test.Id}:assertionsWithHelpers"] = assessment.Assertions;
            result[$"method:{assessment.Test.Id}:branchCountWithHelpers"] = assessment.Branches;
            result[$"method:{assessment.Test.Id}:parameterized"] = assessment.Test.IsParameterized ? 1 : 0;
        }
        return result;
    }

    private static MethodFact[] Helpers(MethodFact test, IEnumerable<MethodFact> fixtures, Dictionary<string, MethodFact> lookup)
    {
        var visited = new Dictionary<string, MethodFact>(StringComparer.Ordinal);
        var pending = new Stack<MethodFact>(fixtures.Append(test));
        while (pending.TryPop(out var method))
        {
            if (!visited.TryAdd(method.Id, method)) continue;
            foreach (var helper in method.Calls.Where(lookup.ContainsKey).Select(call => lookup[call])) pending.Push(helper);
        }
        return visited.Values.OrderBy(m => m.Id, StringComparer.Ordinal).ToArray();
    }
}
