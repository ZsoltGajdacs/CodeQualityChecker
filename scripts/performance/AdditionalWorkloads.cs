using Cqc.Core;
using Cqc.Java;
using Cqc.Server.Reporting;
using System.Text.Json;

internal static class AdditionalWorkloads
{
    internal static async Task JavaModules(PerformanceSamples samples)
    {
        var root = Path.Combine(samples.DirectoryPath, "modules");
        var files = new List<string>();
        var contexts = new List<object>();
        for (var index = 0; index < 40; index++)
        {
            var sourceRoot = $"m{index}/src/main/java";
            var file = $"{sourceRoot}/p{index}/Type{index}.java";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, file))!);
            var target = index == 0 ? "value(n)" : $"p{index - 1}.Type{index - 1}.value(n)";
            var methods = string.Join(" ", Enumerable.Range(0, 15).Select(n => $"public int run{n}(int n) {{ return {target}; }}"));
            await File.WriteAllTextAsync(Path.Combine(root, file), $"package p{index}; public class Type{index} {{ public static int value(int n) {{ return n; }} {methods} }}");
            files.Add(file);
            contexts.Add(new { id = $"m{index}/pom.xml#main", modulePath = $"m{index}/pom.xml", sourceSet = "main",
                sourceRoots = new[] { sourceRoot }, classpath = Array.Empty<object>(),
                reachableContexts = index == 0 ? Array.Empty<string>() : [$"m{index - 1}/pom.xml#main"] });
        }
        Directory.CreateDirectory(Path.Combine(root, ".cqc"));
        await File.WriteAllTextAsync(Path.Combine(root, ".cqc/java-contexts.json"), JsonSerializer.Serialize(new { schemaVersion = "2", sourceFingerprint = "source", contexts }));
        var snapshot = new ProjectSnapshot(root, files.ToArray(), files.ToArray(), [], "source", []);
        var facts = await samples.Measure("java-modules.java", () => new JavaAnalyzer().AnalyzeAsync(snapshot, default));
        if (!facts.SemanticComplete || facts.Types.Length != 40) throw new InvalidOperationException("Java workload lost complete resolution.");
    }

    internal static async Task MaximumPage(PerformanceSamples samples)
    {
        var diagnostics = Enumerable.Range(0, 400).Select(i => new Diagnostic("detail", i + string.Concat(Enumerable.Repeat("\"\\😀<>&", 150)))).ToArray();
        var report = new InspectionResponse("2", Wire.Target, new("/root", [], [], [], "source", diagnostics), []);
        using var store = new ReportStore(TimeProvider.System);
        var summary = await store.PublishAsync(report, default);
        await samples.Measure("byte-budget.page", async () =>
        {
            var page = await store.ReadPageAsync(new() { ReportId = summary.ReportId, Section = "diagnostics", Limit = 200 }, default);
            return new { page.Section, page.Items, page.Total, HasNext = page.NextCursor is not null };
        });
    }
}
