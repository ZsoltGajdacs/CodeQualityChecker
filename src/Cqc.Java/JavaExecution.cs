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
        foreach (var module in MavenBuildRoots.Select(modules, scratchDirectory))
            await BuildAsync(scratchDirectory, module, versions, cancellationToken);
        var classpath = Directory.GetFiles(scratchDirectory, "cqc-classpath.txt", SearchOption.AllDirectories)
            .Where(FreshBuildOutput)
            .SelectMany(File.ReadAllLines).SelectMany(line => line.Split(Path.PathSeparator)).Where(File.Exists);
        await new ArtifactCache(snapshot).ReplaceLinesAsync("java-classpath.txt", classpath, cancellationToken);
        var reports = Directory.GetFiles(scratchDirectory, "jacoco.xml", SearchOption.AllDirectories).Where(FreshBuildOutput).Order(StringComparer.Ordinal).ToArray();
        return new(Language, reports, versions);
    }

    private static bool FreshBuildOutput(string file) => file.Split(Path.DirectorySeparatorChar).Contains("target", StringComparer.Ordinal);

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
        versions["maven"] = (await Required("mvn", ["--version"], directory, token)).Output.Trim();
        await Required("mvn", ["-B", "-f", project, $"org.jacoco:jacoco-maven-plugin:{JacocoVersion}:prepare-agent", "test",
            $"org.jacoco:jacoco-maven-plugin:{JacocoVersion}:report", DependencyPlugin,
            "-Dmdep.outputFile=target/cqc-classpath.txt", "-DincludeScope=test"], directory, token);
    }

    private static async Task<CommandResult> Required(string executable, string[] args, string directory, CancellationToken token)
    {
        var result = await ProcessRunner.RunAsync(executable, args, directory, token);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{executable} exited {result.ExitCode}: {result.Error}\n{result.Output}");
        return result;
    }
}
