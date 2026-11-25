using System.ComponentModel;
using System.Text.Json;
using Cqc.Core;

namespace Cqc.TypeScript;

public sealed class TypeScriptAnalyzer : ILanguageAnalyzer, ILanguageDiscovery
{
    private const string LanguageName = "typescript";

    public string Language => LanguageName;
    public LanguageDefinition Definition { get; } = new()
    {
        Language = LanguageName,
        SourceExtensions = [".ts", ".tsx"],
        ManifestPatterns = ["tsconfig*.json"],
        GeneratedSuffixes = [".d.ts"],
        BuildInputPatterns = ["**/package.json", "**/package-lock.json", "**/npm-shrinkwrap.json",
            "**/pnpm-lock.yaml", "**/pnpm-workspace.yaml", "**/yarn.lock", "**/bun.lock", "**/bun.lockb",
            "**/*.d.mts", "**/*.d.cts"],
        ParticipatesInExecution = false,
        InspectManifest = (_, path) => new(LanguageName, path, "typescript-config", false),
        InspectPrerequisites = (_, _) => TypeScriptWorker.Prerequisites()
    };

    public async Task<AnalysisFacts> AnalyzeAsync(ProjectSnapshot snapshot, CancellationToken cancellationToken)
    {
        var files = snapshot.Files.Where(Definition.IsSource).ToArray();
        if (files.Length == 0) return new([], [], [], [], true);
        var worker = TypeScriptWorker.Find();
        if (worker.Command is null) return Unavailable("typescript.worker_missing", worker.Message);

        var input = snapshot with
        {
            Files = files,
            SelectedFiles = snapshot.SelectedFiles.Where(Definition.IsSource).ToArray(),
            Modules = snapshot.Modules.Where(module => module.Language == LanguageName).ToArray(),
            SchemaVersion = Wire.SchemaVersion
        };
        try
        {
            var result = await ProcessRunner.RunAsync(worker.Command with
            {
                Directory = snapshot.Root,
                Input = JsonSerializer.Serialize(input, Wire.Json)
            }, cancellationToken);
            if (result.ExitCode != 0)
                return Unavailable("typescript.worker_failed", $"TypeScript worker exited with code {result.ExitCode}. {result.Error.Trim()}");
            return AddUnavailableChecks(TypeScriptWorker.ReadFacts(result.Output, files.Length), snapshot.SourceOnly);
        }
        catch (JsonException)
        {
            return Unavailable("typescript.worker_protocol", "Invalid TypeScript worker facts. Install matching server and worker artifacts.");
        }
        catch (Exception exception) when (exception is Win32Exception or IOException)
        {
            return Unavailable("typescript.worker_failed", exception.Message);
        }
    }

    private static AnalysisFacts AddUnavailableChecks(AnalysisFacts facts, bool sourceOnly) => sourceOnly ? facts : facts with
    {
        Diagnostics = facts.Diagnostics.Append(new Diagnostic("typescript.test_coverage_unavailable",
            "The TypeScript integration supports static complexity, duplication and dependencies. Test execution, test-quality scoring and coverage collection are unavailable.", Language: LanguageName)).ToArray()
    };

    private static AnalysisFacts Unavailable(string code, string message) =>
        new([], [], [], [new(code, message, Language: LanguageName) { IsAnalyzerFailure = true }], false);
}
