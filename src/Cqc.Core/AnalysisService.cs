using System.Text.Json;

namespace Cqc.Core;

public sealed class AnalysisService
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);
    private readonly LanguageDefinition[] definitions;
    private readonly LanguageFactCollection factCollection;
    private readonly BuildExecutor buildExecutor;
    private readonly CoverageCollection coverageCollection;

    public AnalysisService(IEnumerable<ILanguageAnalyzer> analyzers)
    {
        var adapters = analyzers.ToArray();
        definitions = adapters.OfType<ILanguageDiscovery>().Select(adapter => adapter.Definition).ToArray();
        factCollection = new(adapters, definitions);
        buildExecutor = new(adapters.OfType<ILanguageExecution>());
        coverageCollection = new(adapters);
    }

    public async Task<InspectionResponse> InspectAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        using var timeout = Timeout(request, cancellationToken);
        var (config, _) = Configuration.Load(Path.GetFullPath(request.ProjectRoot), request.ConfigPath);
        var snapshot = await ProjectDiscovery.DiscoverAsync(request, config, timeout.Token, definitions);
        return new(Wire.SchemaVersion, Wire.Target, snapshot with { Diagnostics = DiagnosticScope.Classify(snapshot, snapshot.Diagnostics) },
            DiagnosticScope.Classify(snapshot, Prerequisites(snapshot)));
    }

    private Diagnostic[] Prerequisites(ProjectSnapshot snapshot)
    {
        var applicable = definitions.Where(d => snapshot.Modules.Any(m => m.Language == d.Language)).ToArray();
        var tools = applicable.SelectMany(d => d.BuildTools).Distinct().Where(tool => !OnPath(tool))
            .Select(tool => new Diagnostic("missing_build_tool", $"Install {tool} to enable explicit project execution. Analysis-only remains available."));
        return tools.Concat(applicable.SelectMany(d => d.InspectPrerequisites?.Invoke(snapshot, OnPath) ?? [])).ToArray();
    }

    public Task<AnalysisResponse> AnalyzeAsync(AnalysisRequest request, string[] families, CancellationToken cancellationToken)
        => AnalyzeAsync(request, AnalysisPolicy.ForChecks(request, families), cancellationToken);

    public Task<AnalysisResponse> ReviewAsync(AnalysisRequest request, CancellationToken cancellationToken)
        => AnalyzeAsync(request, AnalysisPolicy.ForReview(request), cancellationToken);

    private async Task<AnalysisResponse> AnalyzeAsync(AnalysisRequest request, AnalysisPolicy policy, CancellationToken cancellationToken)
    {
        using var timeout = Timeout(request, cancellationToken);
        var root = ProjectPaths.ResolveDirectory(new DirectoryInfo(request.ProjectRoot));
        var gate = Gates.GetOrAdd(root, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(timeout.Token);
        try { return await AnalyzeLockedAsync(request, policy, timeout.Token); }
        finally { gate.Release(); }
    }

    private async Task<AnalysisResponse> AnalyzeLockedAsync(AnalysisRequest request, AnalysisPolicy policy, CancellationToken token)
    {
        var (config, configFingerprint) = Configuration.Load(Path.GetFullPath(request.ProjectRoot), request.ConfigPath);
        var snapshot = (await ProjectDiscovery.DiscoverAsync(request, config, token, definitions))
            with { SourceOnly = policy.SourceOnly };
        var discoveryDiagnostics = DiagnosticScope.Classify(snapshot, DiscoveryDiagnostics(snapshot));
        var diagnostics = new List<Diagnostic>(discoveryDiagnostics);
        var report = new AnalysisResponse
        {
            SchemaVersion = Wire.SchemaVersion, RulesetVersion = "3", Message = Wire.Target,
            SourceFingerprint = snapshot.SourceFingerprint, ConfigurationFingerprint = configFingerprint,
            Scope = request.Scope, AnalyzedFiles = snapshot.SelectedFiles, Modules = snapshot.Modules,
            Diagnostics = discoveryDiagnostics, SourceOnly = snapshot.SourceOnly
        };
        if (policy.ReviewTouchedCode && snapshot.SelectedFiles.Length == 0) return report;
        var changedCode = policy.ReviewTouchedCode ? await ChangedCodeScope.ReadAsync(snapshot, token) : null;

        using var artifacts = policy.Executes ? await BuildAsync(snapshot, diagnostics, token) : null;
        if (policy.Executes) await VerifyBuildSnapshot(request, config, snapshot, token);
        using var savedEvidence = await OpenSavedEvidenceAsync(snapshot, artifacts, diagnostics, token);
        var analysisSnapshot = EvidenceForAnalysis(snapshot, artifacts, savedEvidence);
        var facts = await factCollection.CollectAsync(analysisSnapshot, diagnostics, token);
        diagnostics.AddRange(StructuralDiagnostics.For(facts, snapshot));
        var coverage = await CollectCoverage(policy, new CoverageInput
        {
            Snapshot = snapshot, ConfigurationFingerprint = configFingerprint, ReportPath = request.ReportPath,
            Facts = facts, Artifacts = artifacts, SavedEvidence = savedEvidence
        }, diagnostics, token);
        var results = QualityEngine.Evaluate(new EvaluationRequest
        {
            Snapshot = snapshot, Facts = facts, Config = config, Evidence = coverage,
            ConfigurationFingerprint = configFingerprint, Families = policy.Families, CancellationToken = token, ChangedCode = changedCode
        });
        var verified = await VerifySnapshot(request, config, snapshot, token);
        await ChangedCodeScope.VerifyAsync(changedCode, verified, token);
        token.ThrowIfCancellationRequested();
        if (artifacts is not null && coverage is not null)
            await EvidenceStore.PublishRunAsync(new(snapshot.Root), artifacts, coverage, token);
        return report with
        {
            Results = results.Select(family => family with { Diagnostics = DiagnosticScope.Classify(snapshot, family.Diagnostics) }).ToArray(),
            Diagnostics = DiagnosticScope.Classify(snapshot, diagnostics)
        };
    }

    private static Diagnostic[] DiscoveryDiagnostics(ProjectSnapshot snapshot) => snapshot.SourceOnly
        ? snapshot.Diagnostics.Where(diagnostic => diagnostic.Code != "unsupported_integration").ToArray()
        : snapshot.Diagnostics;

    private async Task<BuildArtifacts?> BuildAsync(ProjectSnapshot snapshot, List<Diagnostic> diagnostics, CancellationToken token)
    {
        var analysisOnly = definitions.Where(definition => !definition.ParticipatesInExecution)
            .Select(definition => definition.Language).ToHashSet(StringComparer.Ordinal);
        var modules = snapshot.Modules.Where(module => !analysisOnly.Contains(module.Language)).ToArray();
        if (modules.Length == 0 && snapshot.Modules.Length != 0) return null;
        await EvidenceStore.EnsureWritableAsync(snapshot.Root, token);
        try
        {
            var artifacts = await buildExecutor.CollectAsync(snapshot with { Modules = modules }, token);
            diagnostics.AddRange(artifacts.Results.SelectMany(result => result.Diagnostics));
            return artifacts;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            diagnostics.Add(new("execution_failed", ex.Message));
            return null;
        }
    }

    private static async Task<EvidenceSnapshot?> OpenSavedEvidenceAsync(ProjectSnapshot snapshot, BuildArtifacts? artifacts,
        List<Diagnostic> diagnostics, CancellationToken token) =>
        artifacts is null && !snapshot.SourceOnly ? await EvidenceStore.OpenAsync(new(snapshot.Root), diagnostics, token) : null;

    private static ProjectSnapshot EvidenceForAnalysis(ProjectSnapshot snapshot, BuildArtifacts? artifacts, EvidenceSnapshot? saved) =>
        snapshot with { ReferenceEvidenceDirectory = artifacts?.RunDirectory ?? saved?.ReferenceDirectory };

    private async Task<CoverageEvidence?> CollectCoverage(AnalysisPolicy policy, CoverageInput input, List<Diagnostic> diagnostics, CancellationToken token)
    {
        if (policy.Evidence != EvidenceMode.Cached && input.Artifacts is null) return null;
        try { return await coverageCollection.CollectAsync(input, diagnostics, token); }
        catch (Exception ex) when (ex is JsonException or ArgumentException or System.Xml.XmlException)
        {
            diagnostics.Add(new("coverage_invalid", "Coverage evidence is invalid: " + ex.Message));
            return null;
        }
    }

    private async Task VerifyBuildSnapshot(AnalysisRequest request, QualityConfig config, ProjectSnapshot snapshot, CancellationToken token)
    {
        var afterBuild = await ProjectDiscovery.DiscoverAsync(request, config, token, definitions);
        if (afterBuild.SourceFingerprint != snapshot.SourceFingerprint)
            throw new InvalidOperationException("Source/build inputs changed during execution. Evidence is not accepted; rerun against a stable snapshot.");
    }

    private async Task<ProjectSnapshot> VerifySnapshot(AnalysisRequest request, QualityConfig config, ProjectSnapshot snapshot,
        CancellationToken token)
    {
        var after = await ProjectDiscovery.DiscoverAsync(request, config, token, definitions);
        if (after.SourceFingerprint != snapshot.SourceFingerprint)
            throw new InvalidOperationException("Source/build inputs changed during analysis. No scores are accepted; rerun against a stable snapshot.");
        return after;
    }

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
