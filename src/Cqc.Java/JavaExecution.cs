using Cqc.Core;

namespace Cqc.Java;

/// <summary>Coordinates Maven execution, staged contexts, and module-owned coverage.</summary>
public sealed class JavaExecution : ILanguageExecution
{
    public const string JacocoVersion = "0.8.14";
    public string Language => "java";

    public async Task<LanguageBuildResult> ExecuteAsync(ProjectSnapshot snapshot, string runDirectory, CancellationToken cancellationToken)
    {
        var modules = snapshot.Modules.Where(module => module.Language == Language).ToArray();
        if (modules.Any(module => !module.Supported))
            throw new InvalidOperationException("Java execution requires supported Maven modules.");
        var versions = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["metadata"] = "4" };
        var runner = new JavaBuildRunner(snapshot.Root, runDirectory, versions);
        var metadata = new List<CollectedModule>();
        foreach (var module in MavenBuildRoots.Select(modules, snapshot.Root))
        {
            var output = await runner.BuildAsync(module, cancellationToken);
            metadata.AddRange(ReadMetadata(output));
        }
        var contexts = metadata.GroupBy(item => MavenPaths.Relative(snapshot.Root, item.Metadata.Pom), StringComparer.Ordinal)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        await new JavaContextCache(snapshot).StageAsync(runDirectory, contexts.Values.Select(item => item.Metadata).ToArray(), cancellationToken);
        foreach (var (module, context) in contexts)
            if (context.Metadata.Coverage?.Version is { } version)
                versions["jacoco:" + module] = version;
        var reports = CollectReports(contexts);
        return new(Language, reports.Keys.ToArray(), versions)
        {
            Diagnostics = CollectionDiagnostics(snapshot, contexts, reports),
            ReportModules = reports,
            ReferenceManifestName = JavaContextCache.FileName
        };
    }

    private sealed record CollectedModule(MavenMetadata Metadata, string Output);

    private static IEnumerable<CollectedModule> ReadMetadata(string reactorDirectory)
    {
        foreach (var directory in Directory.EnumerateDirectories(reactorDirectory, "module-*").Order(StringComparer.Ordinal))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            var path = Path.Combine(directory, "metadata.xml");
            if (IsRegularOutput(path)) yield return new(MavenMetadata.Read(path), directory);
        }
    }

    private static SortedDictionary<string, string> CollectReports(Dictionary<string, CollectedModule> contexts)
    {
        var reports = contexts.Select(item => (Module: item.Key, Report: OwnedReport(item.Value)))
            .Where(item => item.Report is not null)
            .GroupBy(item => item.Report!, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Module, StringComparer.Ordinal);
        return new(reports, StringComparer.Ordinal);
    }

    private static Diagnostic[] CollectionDiagnostics(ProjectSnapshot snapshot, Dictionary<string, CollectedModule> contexts,
        SortedDictionary<string, string> reports)
    {
        var diagnostics = new List<Diagnostic>();
        var covered = reports.Values.ToHashSet(StringComparer.Ordinal);
        foreach (var module in snapshot.Modules.Where(module => module.Language == "java"))
        {
            var context = contexts.GetValueOrDefault(module.Path)?.Metadata;
            if (context is null)
                diagnostics.Add(new("java.classpath_missing", "Maven produced no evaluated metadata for this module; its semantic context remains unverified.", module.Path, "java"));
            if (!covered.Contains(module.Path) && NeedsCoverage(snapshot, module, context))
                diagnostics.Add(new("java.coverage_missing", context?.Coverage?.Reason
                    ?? "Maven produced no module-owned JaCoCo report; tests may be absent or instrumentation unavailable.", module.Path, "java"));
        }
        foreach (var path in contexts.Values.SelectMany(module => module.Metadata.SourceSets).SelectMany(source => source.Classpath).Where(path => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && !File.Exists(path)))
            diagnostics.Add(new("java.classpath_missing", "Maven reported a missing classpath entry: " + path, Language: "java"));
        return diagnostics.ToArray();
    }

    private static bool NeedsCoverage(ProjectSnapshot snapshot, ProjectModule module, MavenMetadata? context)
    {
        var roots = context?.SourceSets.Where(source => source.Name == "main").SelectMany(source => source.SourceRoots)
            .Select(root => MavenPaths.Relative(snapshot.Root, root)).ToArray()
            ?? [MavenPaths.Relative(snapshot.Root, Path.Combine(snapshot.Root, Path.GetDirectoryName(module.Path) ?? "", "src/main/java"))];
        return snapshot.Files.Any(file => file.EndsWith(".java", StringComparison.OrdinalIgnoreCase)
            && roots.Any(root => file.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal)));
    }

    private static string? OwnedReport(CollectedModule module)
    {
        if (module.Metadata.Coverage?.Report is not { } report || !Path.IsPathFullyQualified(report)) return null;
        var path = Path.Combine(module.Output, "jacoco.xml");
        if (!IsRegularOutput(path)) return null;
        return MavenPaths.Canonical(report) == MavenPaths.Canonical(path) ? path : null;
    }

    private static bool IsRegularOutput(string path) =>
        File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

    public MethodCoverage[] ReadCoverage(LanguageBuildResult build, IEnumerable<MethodFact> methods) =>
        ReadCoverage(build, methods, null);

    internal MethodCoverage[] ReadCoverage(LanguageBuildResult build, IEnumerable<MethodFact> methods, ICollection<Diagnostic>? diagnostics)
    {
        var facts = methods.ToArray();
        return build.Reports.SelectMany(report => JavaCoverageReader.ReadJava(report, OwnedMethods(build, report, facts),
                signature => diagnostics?.Add(new("java.coverage_signature_ambiguous",
                    $"Multiple authored methods match JaCoCo signature {signature}.", build.ReportModules.GetValueOrDefault(report), Language)
                    { Construct = signature })))
            .GroupBy(method => method.MethodId, StringComparer.Ordinal).Where(group => group.Count() == 1)
            .Select(group => group.Single()).OrderBy(method => method.MethodId, StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<MethodFact> OwnedMethods(LanguageBuildResult build, string report, MethodFact[] methods)
    {
        if (!build.ReportModules.TryGetValue(report, out var module)) return methods.Where(method => method.ContextId is null);
        return methods.Where(method => method.ContextId == module + "#main");
    }
}
