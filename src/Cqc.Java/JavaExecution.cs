using Cqc.Core;

namespace Cqc.Java;

/// <summary>Maven build and JaCoCo integration; called only after explicit execution authorization.</summary>
public sealed class JavaExecution : ILanguageExecution
{
    public const string JacocoVersion = "0.8.14";
    private const string DependencyPlugin = "org.apache.maven.plugins:maven-dependency-plugin:3.9.0:build-classpath";
    public string Language => "java";

    public async Task<LanguageBuildResult> ExecuteAsync(ProjectSnapshot snapshot, string scratchDirectory, CancellationToken cancellationToken)
    {
        var modules = snapshot.Modules.Where(m => m.Language == Language).ToArray();
        if (modules.Any(m => !m.Supported))
            throw new InvalidOperationException("Java execution requires supported Maven modules.");
        var versions = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["jacoco"] = JacocoVersion };
        var diagnostics = new List<Diagnostic>();
        var classpathFiles = new List<string>();
        var reports = new List<string>();
        foreach (var module in MavenBuildRoots.Select(modules, scratchDirectory))
        {
            var outputs = await CollectRootAsync(snapshot, scratchDirectory, module, versions, cancellationToken);
            diagnostics.AddRange(outputs.Diagnostics);
            classpathFiles.AddRange(outputs.Classpaths);
            reports.AddRange(outputs.Reports);
        }
        var classpath = classpathFiles.SelectMany(File.ReadAllLines).SelectMany(line => line.Split(Path.PathSeparator)).ToArray();
        foreach (var entry in classpath.Where(path => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && !File.Exists(path)))
            diagnostics.Add(new("java.classpath_missing", "Maven reported a missing classpath JAR: " + entry, Language: Language));
        var cache = new ArtifactCache(snapshot);
        if (diagnostics.Any(d => d.Code == "java.classpath_missing")) await cache.InvalidateAsync("java-classpath.txt", cancellationToken);
        else await cache.ReplaceLinesAsync("java-classpath.txt", PersistClasspath(classpath, scratchDirectory, cache), cancellationToken);
        return new(Language, reports.Order(StringComparer.Ordinal).ToArray(), versions) { Diagnostics = diagnostics.ToArray() };
    }

    private sealed record RootOutputs(string[] Classpaths, string[] Reports, Diagnostic[] Diagnostics);

    private static async Task<RootOutputs> CollectRootAsync(ProjectSnapshot snapshot, string scratch, ProjectModule module,
        SortedDictionary<string, string> versions, CancellationToken token)
    {
        var originalTool = MavenTool.Select(snapshot.Root, Path.Combine(snapshot.Root, module.Path));
        if (originalTool != "mvn" && (File.GetAttributes(originalTool) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Maven Wrapper must be a regular executable file: " + originalTool);
        var beforeClasspath = FindOutputs(scratch, "cqc-classpath.txt");
        var beforeReports = FindOutputs(scratch, "jacoco.xml");
        await BuildAsync(scratch, module, versions, token);
        var classpaths = FindOutputs(scratch, "cqc-classpath.txt").Except(beforeClasspath, StringComparer.Ordinal).ToArray();
        var reports = FindOutputs(scratch, "jacoco.xml").Except(beforeReports, StringComparer.Ordinal).ToArray();
        var diagnostics = new List<Diagnostic>();
        if (classpaths.Length == 0) diagnostics.Add(new("java.classpath_missing", "Maven did not produce a test classpath report; check dependency plugin execution.", module.Path, "java"));
        if (reports.Length == 0) diagnostics.Add(new("java.coverage_missing", "Maven produced no JaCoCo report; tests may be absent or coverage instrumentation unavailable.", module.Path, "java"));
        return new(classpaths, reports, diagnostics.ToArray());
    }

    private static string[] FindOutputs(string root, string name) => Directory.GetFiles(root, name, SearchOption.AllDirectories).Where(FreshBuildOutput).ToArray();

    private static bool FreshBuildOutput(string file) => file.Split(Path.DirectorySeparatorChar).Contains("target", StringComparer.Ordinal);

    internal static IEnumerable<string> PersistClasspath(IEnumerable<string> entries, string scratchDirectory, ArtifactCache cache)
    {
        var scratchPrefix = Path.GetFullPath(scratchDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var entry in entries.Where(path => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && File.Exists(path)))
        {
            var full = Path.GetFullPath(entry);
            yield return full.StartsWith(scratchPrefix, StringComparison.Ordinal) ? cache.PersistFile(full) : full;
        }
    }

    public MethodCoverage[] ReadCoverage(LanguageBuildResult build, IEnumerable<MethodFact> methods)
    {
        var facts = methods.ToArray();
        // Separate reports might describe different executions or overlapping aggregate results.
        // Without branch identities an arbitrary best-result selection would overstate evidence.
        return build.Reports.SelectMany(report => JavaCoverageReader.ReadJava(report, facts))
            .GroupBy(method => method.MethodId, StringComparer.Ordinal).Where(group => group.Count() == 1)
            .Select(group => group.Single()).OrderBy(method => method.MethodId, StringComparer.Ordinal).ToArray();
    }

    private static async Task BuildAsync(string scratch, ProjectModule module,
        SortedDictionary<string, string> versions, CancellationToken token)
    {
        var project = Path.Combine(scratch, module.Path);
        var directory = Path.GetDirectoryName(project)!;
        var tool = MavenTool.Select(scratch, project);
        versions["maven:" + module.Path] = (await MavenTool.RunRequired(tool, ["--version"], directory, token)).Output.Trim();
        await MavenTool.RunRequired(tool, ["-B", "-f", project, $"org.jacoco:jacoco-maven-plugin:{JacocoVersion}:prepare-agent", "test",
            $"org.jacoco:jacoco-maven-plugin:{JacocoVersion}:report", DependencyPlugin,
            "-Dmdep.outputFile=target/cqc-classpath.txt", "-DincludeScope=test"], directory, token);
    }

}
