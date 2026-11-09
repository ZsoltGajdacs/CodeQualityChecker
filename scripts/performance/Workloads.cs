using Cqc.Core;

internal static class Workloads
{
    private static readonly EvidenceState Verified = new(true, true, true, true, true, []);

    internal static (ProjectSnapshot Snapshot, AnalysisFacts Facts) Create(string root, string shape)
    {
        var count = shape == "dense" ? 400 : 1200;
        var types = Enumerable.Range(0, count).Select(i => new TypeFact("Type" + i, "csharp", new($"src/Type{i}.cs", 1, 30), i % 5 == 0)
        { Evidence = Verified, ContextId = "module" + i / 6, IsTest = shape == "helpers" }).ToArray();
        var methods = types.Select((type, i) => new MethodFact(type.Id + ".Run()", type.Id, "csharp", new(type.Location.File, 2, 25), 10,
            20, shape == "helpers", false, false, 1, shape == "helpers" ? ["Type0.Run()", "Type1.Run()"] : [],
            Enumerable.Range(0, 25).Select(n => shape == "dense" ? "shared" + n : $"group{i / 3}:node{n}").ToArray(), 30)
        { Evidence = Verified, ContextId = type.ContextId }).ToArray();
        var contexts = Enumerable.Range(0, count / 6).Select(i => new AnalysisContext("module" + i, "module" + i, "main",
            i > 0 && shape == "contexts" ? ["module" + (i - 1)] : [], true, [])).ToArray();
        var edges = types.Skip(1).Select((type, i) => new DependencyFact(type.Id, types[i].Id, type.Location)).ToArray();
        var facts = new AnalysisFacts(types, methods, edges, [], true) { Contexts = contexts };
        var files = types.Select(t => t.Location.File).ToArray();
        return (new(root, files, files, [], "source", []), facts);
    }
}
