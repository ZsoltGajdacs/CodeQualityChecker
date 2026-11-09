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
        var versions = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["jacoco"] = JacocoVersion, ["metadata"] = "2" };
        var runner = new JavaBuildRunner(snapshot, scratchDirectory, versions);
        foreach (var module in MavenBuildRoots.Select(modules, scratchDirectory))
            await runner.BuildAsync(module, cancellationToken);
        var metadata = Directory.GetFiles(scratchDirectory, "cqc-metadata.xml", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).Select(MavenMetadata.Read).ToArray();
        await new JavaContextCache(snapshot, scratchDirectory).PublishAsync(metadata, cancellationToken);
        var reports = ReportOwnership(metadata, scratchDirectory);
        var diagnostics = CollectionDiagnostics(snapshot, metadata, reports, scratchDirectory);
        return new(Language, reports.Keys.ToArray(), versions) { Diagnostics = diagnostics, ReportModules = reports };
    }

    private static Diagnostic[] CollectionDiagnostics(ProjectSnapshot snapshot, MavenMetadata[] metadata,
        SortedDictionary<string, string> reports, string scratch)
    {
        var captured = metadata.Select(module => Relative(scratch, module.Pom)).ToHashSet(StringComparer.Ordinal);
        var diagnostics = new List<Diagnostic>();
        foreach (var module in snapshot.Modules.Where(module => module.Language == "java"))
        {
            if (!captured.Contains(module.Path)) diagnostics.Add(new("java.classpath_missing", "Maven produced no evaluated metadata for this module; its semantic context remains unverified.", module.Path, "java"));
            if (NeedsCoverage(snapshot, module, metadata, scratch) && !reports.Values.Contains(module.Path, StringComparer.Ordinal)) diagnostics.Add(new("java.coverage_missing", "Maven produced no JaCoCo report for this module; tests may be absent or instrumentation unavailable.", module.Path, "java"));
        }
        foreach (var path in metadata.SelectMany(module => module.SourceSets).SelectMany(source => source.Classpath).Where(path => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && !File.Exists(path)))
            diagnostics.Add(new("java.classpath_missing", "Maven reported a missing classpath entry: " + path, Language: "java"));
        return diagnostics.ToArray();
    }

    private static bool NeedsCoverage(ProjectSnapshot snapshot, ProjectModule module, MavenMetadata[] metadata, string scratch)
    {
        var captured = metadata.FirstOrDefault(item => Relative(scratch, item.Pom) == module.Path);
        var roots = captured?.SourceSets.Where(source => source.Name == "main").SelectMany(source => source.SourceRoots)
            .Select(root => Relative(scratch, root)).ToArray()
            ?? [Relative(scratch, Path.Combine(scratch, Path.GetDirectoryName(module.Path) ?? "", "src/main/java"))];
        return snapshot.Files.Any(file => file.EndsWith(".java", StringComparison.OrdinalIgnoreCase)
            && roots.Any(root => file.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal)));
    }

    private static SortedDictionary<string, string> ReportOwnership(MavenMetadata[] modules, string scratch)
    {
        var reports = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            if (!Directory.Exists(module.BuildDirectory)) continue;
            foreach (var report in Directory.GetFiles(module.BuildDirectory, "jacoco.xml", SearchOption.AllDirectories))
                reports.TryAdd(report, Relative(scratch, module.Pom));
        }
        return reports;
    }

    private static string Relative(string root, string path) => MavenPaths.Relative(root, path);

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
