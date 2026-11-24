using System.Text.Json;
using Cqc.Core;

namespace Cqc.Java;

public sealed class JavaAnalyzer : ILanguageAnalyzer, ILanguageExecution, ILanguageDiscovery
{
    private const string SourceOnlySchemaVersion = "3";
    public string Language => "java";
    public LanguageDefinition Definition { get; } = new()
    {
        Language = "java", SourceExtensions = [".java"], ManifestPatterns = ["pom.xml"],
        InspectManifest = (_, path) => new("java", path, "maven", true),
        BuildInputPatterns = ["**/.mvn/**", "**/settings.xml", "**/mvnw", "**/mvnw.cmd"],
        IgnoredBuildInputPatterns = ["**/.mvn/wrapper/**", "**/mvnw", "**/mvnw.cmd"],
        ExecutableBuildInputPatterns = ["**/mvnw"],
        BuildTools = ["java"], InspectPrerequisites = MavenTool.InspectPrerequisites
    };

    public Task<LanguageBuildResult> ExecuteAsync(ProjectSnapshot snapshot, string runDirectory, CancellationToken cancellationToken)
        => new JavaExecution().ExecuteAsync(snapshot, runDirectory, cancellationToken);

    public MethodCoverage[] ReadCoverage(LanguageBuildResult build, IEnumerable<MethodFact> methods)
        => new JavaExecution().ReadCoverage(build, methods);

    public async Task<AnalysisFacts> AnalyzeAsync(ProjectSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!snapshot.Files.Any(path => path.EndsWith(".java", StringComparison.OrdinalIgnoreCase)))
            return new([], [], [], [], true);
        var jar = FindWorker();
        if (jar is null)
            return JavaWorkerFacts.Unavailable("java.worker_missing", "Build java-worker with Maven or install the packaged server containing java-worker.jar.");
        var bundled = Path.Combine(AppContext.BaseDirectory, "runtime", "bin", "java");
        var version = snapshot.SourceOnly ? SourceOnlySchemaVersion : Wire.SchemaVersion;
        try
        {
            var command = new ProcessCommand
            {
                Executable = File.Exists(bundled) ? bundled : "java",
                Arguments = ["-jar", jar],
                Directory = snapshot.Root,
                Input = JsonSerializer.Serialize(snapshot with { SchemaVersion = version }, Wire.Json)
            };
            var result = await ProcessRunner.RunAsync(command, cancellationToken);
            if (result.ExitCode != 0)
                return JavaWorkerFacts.Unavailable("java.worker_failed", "Java analyzer failed: " + result.Error.Trim());
            return JavaWorkerFacts.Read(result.Output, version);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or JsonException)
        {
            return JavaWorkerFacts.Unavailable("java.worker_failed", exception.Message);
        }
    }

    private static string? FindWorker()
    {
        var configured = Environment.GetEnvironmentVariable("CQC_JAVA_WORKER");
        if (!string.IsNullOrEmpty(configured)) return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        var packaged = Path.Combine(AppContext.BaseDirectory, "java-worker.jar");
        if (File.Exists(packaged)) return packaged;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "java-worker", "target", "java-worker.jar");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
