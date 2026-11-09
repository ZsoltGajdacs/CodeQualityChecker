using Cqc.Core;
using Cqc.CSharp;
using Cqc.Java;
using Cqc.Server.Reporting;
using System.Text.Json;

if (args.Length != 2) throw new ArgumentException("Usage: Cqc.Performance <stable-input-root> <output.json>");
var root = Path.GetFullPath(args[0]);
using var samples = new PerformanceSamples();
var definitions = new ILanguageDiscovery[] { new CSharpAnalyzer(), new JavaAnalyzer() }.Select(x => x.Definition).ToArray();
foreach (var (name, path) in new[] { ("fixture", "fixtures/mixed"), ("repository", ".") })
{
    var request = new AnalysisRequest { ProjectRoot = Path.Combine(root, path) };
    var snapshot = await samples.Measure($"{name}.discovery", () => ProjectDiscovery.DiscoverAsync(request, new(), default, definitions));
    await samples.Measure($"{name}.csharp", () => new CSharpAnalyzer().AnalyzeAsync(snapshot, default));
    await samples.Measure($"{name}.java", () => new JavaAnalyzer().AnalyzeAsync(snapshot, default));
    await samples.Measure($"{name}.check_all", () => new AnalysisService([new CSharpAnalyzer(), new JavaAnalyzer()])
        .AnalyzeAsync(request, ["code", "tests", "duplication", "dependencies"], default));
}
await AdditionalWorkloads.JavaModules(samples);
await AdditionalWorkloads.MaximumPage(samples);
foreach (var shape in new[] { "sparse", "dense", "contexts", "helpers" })
{
    var (snapshot, facts) = Workloads.Create(root, shape);
    FamilyResult[] results = [];
    foreach (var family in new[] { "code", "tests", "duplication", "dependencies" })
        results = results.Concat(await samples.Measure($"{shape}.{family}", () => Task.FromResult(QualityEngine.Evaluate(new()
        { Snapshot = snapshot, Facts = facts, ConfigurationFingerprint = "config", Families = [family] })))).ToArray();
    var report = new AnalysisResponse("2", "2", Wire.Target, "source", "config", "repository", snapshot.Files, [], results, []);
    using var store = new ReportStore(TimeProvider.System);
    await samples.Measure($"{shape}.publish", async () =>
    {
        var summary = await store.PublishAsync(report, default);
        return summary with { ReportId = "report", ExpiresAt = DateTimeOffset.UnixEpoch };
    });
    var published = await store.PublishAsync(report, default);
    await samples.Measure($"{shape}.page", async () =>
    {
        var page = await store.ReadPageAsync(new() { ReportId = published.ReportId, Section = "metrics", Limit = 200 }, default);
        return new { page.Section, page.Total, page.Items, HasNext = page.NextCursor is not null };
    });
    samples.Operations(shape, facts);
}
await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(samples.Results, Wire.Json));
