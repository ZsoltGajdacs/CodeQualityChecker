using Cqc.Core;

namespace Cqc.Java;

/// <summary>Coordinates isolated Maven execution, context publication, and module-owned coverage.</summary>
public sealed class JavaExecution : ILanguageExecution
{
    public const string JacocoVersion = "0.8.14";
    public string Language => "java";

    public async Task<LanguageBuildResult> ExecuteAsync(ProjectSnapshot snapshot, string scratchDirectory, CancellationToken cancellationToken)
    {
        var modules = snapshot.Modules.Where(module => module.Language == Language).ToArray();
        if (modules.Any(module => !module.Supported))
            throw new InvalidOperationException("Java execution requires supported Maven modules.");
        var versions = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["metadata"] = "3" };
        var runner = new JavaBuildRunner(snapshot, scratchDirectory, versions);
        foreach (var module in MavenBuildRoots.Select(modules, scratchDirectory))
            await runner.BuildAsync(module, cancellationToken);
        var metadata = Directory.GetFiles(scratchDirectory, "cqc-metadata.xml", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).Select(MavenMetadata.Read).ToArray();
        var contexts = metadata.GroupBy(item => MavenPaths.Relative(scratchDirectory, item.Pom), StringComparer.Ordinal)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        await new JavaContextCache(snapshot, scratchDirectory).PublishAsync(contexts.Values.ToArray(), cancellationToken);
        foreach (var (module, context) in contexts)
            if (context.Coverage?.Version is { } version)
                versions["jacoco:" + module] = version;
        var reports = CollectReports(contexts, scratchDirectory);
        var diagnostics = CollectionDiagnostics(snapshot, contexts, reports, scratchDirectory);
        return new(Language, reports.Keys.ToArray(), versions) { Diagnostics = diagnostics, ReportModules = reports };
    }

    private static SortedDictionary<string, string> CollectReports(Dictionary<string, MavenMetadata> contexts, string scratch)
    {
        var reports = contexts.Select(item => (Module: item.Key, Report: OwnedReport(item.Value, scratch)))
            .Where(item => item.Report is not null)
            .GroupBy(item => item.Report!, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Module, StringComparer.Ordinal);
        return new(reports, StringComparer.Ordinal);
    }

    private static Diagnostic[] CollectionDiagnostics(ProjectSnapshot snapshot, Dictionary<string, MavenMetadata> contexts,
        SortedDictionary<string, string> reports, string scratch)
    {
        var diagnostics = new List<Diagnostic>();
        var covered = reports.Values.ToHashSet(StringComparer.Ordinal);
        foreach (var module in snapshot.Modules.Where(module => module.Language == "java"))
        {
            contexts.TryGetValue(module.Path, out var context);
            if (context is null)
                diagnostics.Add(new("java.classpath_missing", "Maven produced no evaluated metadata for this module; its semantic context remains unverified.", module.Path, "java"));
            if (!covered.Contains(module.Path) && NeedsCoverage(snapshot, module, context, scratch))
                diagnostics.Add(new("java.coverage_missing", context?.Coverage?.Reason
                    ?? "Maven produced no module-owned JaCoCo report; tests may be absent or instrumentation unavailable.", module.Path, "java"));
        }
        foreach (var path in contexts.Values.SelectMany(module => module.SourceSets).SelectMany(source => source.Classpath).Where(path => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && !File.Exists(path)))
            diagnostics.Add(new("java.classpath_missing", "Maven reported a missing classpath entry: " + path, Language: "java"));
        return diagnostics.ToArray();
    }

    private static bool NeedsCoverage(ProjectSnapshot snapshot, ProjectModule module, MavenMetadata? context, string scratch)
    {
        var roots = context?.SourceSets.Where(source => source.Name == "main").SelectMany(source => source.SourceRoots)
            .Select(root => MavenPaths.Relative(scratch, root)).ToArray()
            ?? [MavenPaths.Relative(scratch, Path.Combine(scratch, Path.GetDirectoryName(module.Path) ?? "", "src/main/java"))];
        return snapshot.Files.Any(file => file.EndsWith(".java", StringComparison.OrdinalIgnoreCase)
            && roots.Any(root => file.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal)));
    }

    private static string? OwnedReport(MavenMetadata module, string scratch)
    {
        if (module.Coverage?.Report is not { } report)
            return null;
        if (!Path.IsPathFullyQualified(report) || !File.Exists(report))
            return null;
        var canonical = MavenPaths.Canonical(report);
        return MavenPaths.IsDescendant(scratch, canonical) && MavenPaths.IsDescendant(module.BuildDirectory, canonical)
            ? canonical : null;
    }

    public MethodCoverage[] ReadCoverage(LanguageBuildResult build, IEnumerable<MethodFact> methods)
    {
        var facts = methods.ToArray();
        return build.Reports.SelectMany(report => JavaCoverageReader.ReadJava(report, OwnedMethods(build, report, facts)))
            .GroupBy(method => method.MethodId, StringComparer.Ordinal).Where(group => group.Count() == 1)
            .Select(group => group.Single()).OrderBy(method => method.MethodId, StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<MethodFact> OwnedMethods(LanguageBuildResult build, string report, MethodFact[] methods)
    {
        if (!build.ReportModules.TryGetValue(report, out var module)) return methods.Where(method => method.ContextId is null);
        return methods.Where(method => method.ContextId == module + "#main");
    }
}
