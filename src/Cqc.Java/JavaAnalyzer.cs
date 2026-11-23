using System.Diagnostics;
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

    public Task<LanguageBuildResult> ExecuteAsync(ProjectSnapshot snapshot, string scratchDirectory, CancellationToken cancellationToken)
        => new JavaExecution().ExecuteAsync(snapshot, scratchDirectory, cancellationToken);

    public MethodCoverage[] ReadCoverage(LanguageBuildResult build, IEnumerable<MethodFact> methods)
        => new JavaExecution().ReadCoverage(build, methods);

    public async Task<AnalysisFacts> AnalyzeAsync(ProjectSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!snapshot.Files.Any(path => path.EndsWith(".java", StringComparison.OrdinalIgnoreCase)))
            return new([], [], [], [], true);
        var jar = FindWorker();
        if (jar is null)
            return Unavailable("java.worker_missing", "Build java-worker with Maven or install the packaged server containing java-worker.jar.");
        var bundled = Path.Combine(AppContext.BaseDirectory, "runtime", "bin", "java");
        var start = new ProcessStartInfo(File.Exists(bundled) ? bundled : "java")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false,
            WorkingDirectory = snapshot.Root
        };
        start.ArgumentList.Add("-jar");
        start.ArgumentList.Add(jar);
        var version = snapshot.SourceOnly ? SourceOnlySchemaVersion : Wire.SchemaVersion;
        try
        {
            var workerInput = snapshot with { SchemaVersion = version };
            var result = await JavaWorkerProcess.RunAsync(start, JsonSerializer.Serialize(workerInput, Wire.Json), cancellationToken);
            if (result.ExitCode != 0)
                return Unavailable("java.worker_failed", "Java analyzer failed: " + result.Error.Trim());
            return ReadFacts(result.Output, version);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or JsonException)
        {
            return Unavailable("java.worker_failed", exception.Message);
        }
    }

    private static AnalysisFacts ReadFacts(string output, string expectedVersion)
    {
        using var wire = JsonDocument.Parse(output);
        if (!wire.RootElement.TryGetProperty("schemaVersion", out var version) || version.GetString() != expectedVersion)
            return Unavailable("java.worker_protocol", $"Java worker schemaVersion must be {expectedVersion}. Install matching server and worker artifacts.");
        var facts = JsonSerializer.Deserialize<AnalysisFacts>(output, Wire.Json);
        if (facts is null) return Unavailable("java.worker_empty", "Java analyzer returned no facts.");
        if (facts.Types.Any(type => type.Evidence is null) || facts.Methods.Any(method => method.Evidence is null))
            return Unavailable("java.worker_protocol", "Java facts require explicit evidence. Install matching server and worker artifacts.");
        return facts;
    }

    private static AnalysisFacts Unavailable(string code, string message) =>
        new([], [], [], [new(code, message, Language: "java")], false);

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
