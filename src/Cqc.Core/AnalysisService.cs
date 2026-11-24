using System.Text.Json;

namespace Cqc.Core;

public sealed class AnalysisService(IEnumerable<ILanguageAnalyzer> analyzers)
{
    private static readonly string[] ReviewFamilies = ["code", "tests", "duplication", "dependencies"];
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);
    private readonly ILanguageAnalyzer[] adapters = analyzers.ToArray();
    private LanguageDefinition[] Definitions => adapters.OfType<ILanguageDiscovery>().Select(x => x.Definition).ToArray();
    public async Task<InspectionResponse> InspectAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        using var timeout = Timeout(request, cancellationToken);
        var (config, _) = Configuration.Load(Path.GetFullPath(request.ProjectRoot), request.ConfigPath);
        var snapshot = await ProjectDiscovery.DiscoverAsync(request, config, timeout.Token, Definitions);
        return new(Wire.SchemaVersion, Wire.Target, snapshot with { Diagnostics = DiagnosticScope.Classify(snapshot, snapshot.Diagnostics) },
            DiagnosticScope.Classify(snapshot, Prerequisites(snapshot)));
    }

    private Diagnostic[] Prerequisites(ProjectSnapshot snapshot)
    {
        var definitions = Definitions.Where(d => snapshot.Modules.Any(m => m.Language == d.Language)).ToArray();
        var tools = definitions.SelectMany(d => d.BuildTools).Distinct().Where(tool => !OnPath(tool))
            .Select(tool => new Diagnostic("missing_build_tool", $"Install {tool} to enable explicit project execution. Analysis-only remains available."));
        return tools.Concat(definitions.SelectMany(d => d.InspectPrerequisites?.Invoke(snapshot, OnPath) ?? [])).ToArray();
    }

    public Task<AnalysisResponse> AnalyzeAsync(AnalysisRequest request, string[] families, CancellationToken cancellationToken)
        => AnalyzeAsync(request, families, AnalysisMode.EvidenceAware, cancellationToken);

    public Task<AnalysisResponse> ReviewAsync(AnalysisRequest request, CancellationToken cancellationToken)
        => AnalyzeAsync(request, request.Execute ? ReviewFamilies : ["code", "duplication"],
            request.Execute ? AnalysisMode.Execute : AnalysisMode.SourceOnly, cancellationToken);

    private async Task<AnalysisResponse> AnalyzeAsync(AnalysisRequest request, string[] families, AnalysisMode mode, CancellationToken cancellationToken)
    {
        using var timeout = Timeout(request, cancellationToken);
        var root = ProjectPaths.ResolveDirectory(new DirectoryInfo(request.ProjectRoot));
        var gate = Gates.GetOrAdd(root, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(timeout.Token);
        try { return await AnalyzeLockedAsync(request, families, mode, timeout.Token); }
        finally { gate.Release(); }
    }

    private async Task<AnalysisResponse> AnalyzeLockedAsync(AnalysisRequest request, string[] families, AnalysisMode mode, CancellationToken token)
    {
        var (config, configFingerprint) = Configuration.Load(Path.GetFullPath(request.ProjectRoot), request.ConfigPath);
        var snapshot = (await ProjectDiscovery.DiscoverAsync(request, config, token, Definitions))
            with { SourceOnly = mode == AnalysisMode.SourceOnly };
        var discoveryDiagnostics = DiagnosticScope.Classify(snapshot, DiscoveryDiagnostics(snapshot));
        var diagnostics = new List<Diagnostic>(discoveryDiagnostics);
        var report = new AnalysisResponse
        {
            SchemaVersion = Wire.SchemaVersion, RulesetVersion = "2", Message = Wire.Target,
            SourceFingerprint = snapshot.SourceFingerprint, ConfigurationFingerprint = configFingerprint,
            Scope = request.Scope, AnalyzedFiles = snapshot.SelectedFiles, Modules = snapshot.Modules,
            Diagnostics = discoveryDiagnostics, SourceOnly = snapshot.SourceOnly
        };
        if (mode != AnalysisMode.EvidenceAware && IsEmptyChangeSelection(request, snapshot)) return report;

        using var artifacts = await BuildIfRequestedAsync(request, snapshot, diagnostics, token);
        await VerifyBuildSnapshot(request, config, snapshot, token);
        var analysisSnapshot = artifacts is null ? snapshot : snapshot with { ReferenceEvidenceDirectory = artifacts.RunDirectory };
        var facts = await CollectFacts(analysisSnapshot, diagnostics, token);
        diagnostics.AddRange(StructuralDiagnostics.For(facts, snapshot));
        var coverage = await CollectCoverage(new CoverageInput
        {
            Snapshot = snapshot, ConfigurationFingerprint = configFingerprint, Request = request,
            Facts = facts, Artifacts = artifacts
        }, diagnostics, token);
        var results = QualityEngine.Evaluate(new EvaluationRequest
        {
            Snapshot = snapshot, Facts = facts, Config = config, Evidence = coverage,
            ConfigurationFingerprint = configFingerprint, Families = families, CancellationToken = token
        });
        await VerifySnapshot(request, config, snapshot, token);
        token.ThrowIfCancellationRequested();
        if (artifacts is not null && coverage is not null)
            await EvidenceStore.PublishRunAsync(snapshot.Root, artifacts, coverage, token);
        return report with
        {
            Results = results.Select(family => family with { Diagnostics = DiagnosticScope.Classify(snapshot, family.Diagnostics) }).ToArray(),
            Diagnostics = DiagnosticScope.Classify(snapshot, diagnostics)
        };
    }

    private static bool IsEmptyChangeSelection(AnalysisRequest request, ProjectSnapshot snapshot) =>
        request.Scope == "changed" && snapshot.SelectedFiles.Length == 0;

    private static Diagnostic[] DiscoveryDiagnostics(ProjectSnapshot snapshot) => snapshot.SourceOnly
        ? snapshot.Diagnostics.Where(diagnostic => diagnostic.Code != "unsupported_integration").ToArray()
        : snapshot.Diagnostics;

    private async Task<BuildArtifacts?> BuildIfRequestedAsync(AnalysisRequest request, ProjectSnapshot snapshot, List<Diagnostic> diagnostics, CancellationToken token)
    {
        if (!request.Execute) return null;
        var analysisOnly = Definitions.Where(definition => !definition.ParticipatesInExecution)
            .Select(definition => definition.Language).ToHashSet(StringComparer.Ordinal);
        var modules = snapshot.Modules.Where(module => !analysisOnly.Contains(module.Language)).ToArray();
        if (modules.Length == 0 && snapshot.Modules.Length != 0) return null;
        await EvidenceStore.EnsureWritableAsync(snapshot.Root, token);
        try
        {
            var artifacts = await new BuildExecutor(adapters.OfType<ILanguageExecution>())
                .CollectAsync(snapshot with { Modules = modules }, token);
            diagnostics.AddRange(artifacts.Results.SelectMany(result => result.Diagnostics));
            return artifacts;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            diagnostics.Add(new("execution_failed", ex.Message));
            return null;
        }
    }

    private async Task<CoverageEvidence?> CollectCoverage(CoverageInput input, List<Diagnostic> diagnostics, CancellationToken token)
    {
        if (input.Snapshot.SourceOnly) return null;
        if (input.Request.Execute && input.Artifacts is null) return null;
        try { return await new CoverageCollection(adapters).CollectAsync(input, diagnostics, token); }
        catch (Exception ex) when (ex is JsonException or ArgumentException or System.Xml.XmlException)
        {
            diagnostics.Add(new("coverage_invalid", "Coverage evidence is invalid: " + ex.Message));
            return null;
        }
    }

    private enum AnalysisMode { EvidenceAware, SourceOnly, Execute }

    private async Task VerifyBuildSnapshot(AnalysisRequest request, QualityConfig config, ProjectSnapshot snapshot, CancellationToken token)
    {
        if (!request.Execute) return;
        var afterBuild = await ProjectDiscovery.DiscoverAsync(request, config, token, Definitions);
        if (afterBuild.SourceFingerprint != snapshot.SourceFingerprint)
            throw new InvalidOperationException("Source/build inputs changed during execution. Evidence is not accepted; rerun against a stable snapshot.");
    }

    private async Task VerifySnapshot(AnalysisRequest request, QualityConfig config, ProjectSnapshot snapshot, CancellationToken token)
    {
        var after = await ProjectDiscovery.DiscoverAsync(request, config, token, Definitions);
        if (after.SourceFingerprint != snapshot.SourceFingerprint)
            throw new InvalidOperationException("Source/build inputs changed during analysis. No scores are accepted; rerun against a stable snapshot.");
    }

    private async Task<AnalysisFacts> CollectFacts(ProjectSnapshot snapshot, List<Diagnostic> diagnostics, CancellationToken token)
    {
        var results = new List<AnalysisFacts>();
        foreach (var adapter in adapters.Where(a => Definitions.Where(d => d.Language == a.Language).Any(d => snapshot.Files.Any(d.IsSource))))
        {
            try
            {
                var facts = await adapter.AnalyzeAsync(snapshot, token);
                if (!snapshot.SourceOnly && HasSemanticFailure(facts))
                    facts = facts with { Diagnostics = facts.Diagnostics.Append(new Diagnostic("analyzer_failed", "No analyzable types were produced. Check language diagnostics and installed analyzer runtimes.", Language: adapter.Language)).ToArray() };
                results.Add(LanguageIdentity.Qualify(facts, adapter.Language));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException)
            {
                if (!snapshot.SourceOnly) diagnostics.Add(new("analyzer_failed", ex.Message, Language: adapter.Language));
                results.Add(new([], [], [], [new("analyzer_failed", ex.Message, Language: adapter.Language)], false));
            }
        }
        return new(results.SelectMany(r => r.Types).ToArray(), results.SelectMany(r => r.Methods).ToArray(),
            results.SelectMany(r => r.Dependencies).ToArray(), results.SelectMany(r => r.Diagnostics).ToArray(), results.All(r => r.SemanticComplete))
        {
            Contexts = results.SelectMany(r => r.Contexts).ToArray(),
            SyntaxGaps = results.SelectMany(r => r.SyntaxGaps).ToArray(),
            Executables = results.SelectMany(r => r.Executables).ToArray()
        };
    }

    private static bool HasSemanticFailure(AnalysisFacts facts) => !facts.SemanticComplete && facts.Types.Length == 0;

    private static CancellationTokenSource Timeout(AnalysisRequest request, CancellationToken token)
    {
        if (request.TimeoutSeconds is < 1 or > 3600) throw new ArgumentException("Timeout must be between 1 and 3600 seconds.");
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
        return timeout;
    }

    private static bool OnPath(string tool) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Any(p => File.Exists(Path.Combine(p, tool)));
}
