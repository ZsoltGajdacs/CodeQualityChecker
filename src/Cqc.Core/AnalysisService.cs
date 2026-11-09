using System.Text.Json;

namespace Cqc.Core;

public sealed class AnalysisService(IEnumerable<ILanguageAnalyzer> analyzers)
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);
    private readonly ILanguageAnalyzer[] adapters = analyzers.ToArray();
    private LanguageDefinition[] Definitions => adapters.OfType<ILanguageDiscovery>().Select(x => x.Definition).ToArray();
    public async Task<InspectionResponse> InspectAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        using var timeout = Timeout(request, cancellationToken);
        var (config, _) = Configuration.Load(Path.GetFullPath(request.ProjectRoot), request.ConfigPath);
        var snapshot = await ProjectDiscovery.DiscoverAsync(request, config, timeout.Token, Definitions);
        return new(Wire.SchemaVersion, Wire.Target, snapshot, Prerequisites(snapshot));
    }

    private Diagnostic[] Prerequisites(ProjectSnapshot snapshot)
    {
        var definitions = Definitions.Where(d => snapshot.Modules.Any(m => m.Language == d.Language)).ToArray();
        var tools = definitions.SelectMany(d => d.BuildTools).Distinct().Where(tool => !OnPath(tool))
            .Select(tool => new Diagnostic("missing_build_tool", $"Install {tool} to enable explicit project execution. Analysis-only remains available."));
        return tools.Concat(definitions.SelectMany(d => d.InspectPrerequisites?.Invoke(snapshot, OnPath) ?? [])).ToArray();
    }

    public async Task<AnalysisResponse> AnalyzeAsync(AnalysisRequest request, string[] families, CancellationToken cancellationToken)
    {
        using var timeout = Timeout(request, cancellationToken);
        var gate = Gates.GetOrAdd(Path.GetFullPath(request.ProjectRoot), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(timeout.Token);
        try { return await AnalyzeLockedAsync(request, families, timeout.Token); }
        finally { gate.Release(); }
    }

    private async Task<AnalysisResponse> AnalyzeLockedAsync(AnalysisRequest request, string[] families, CancellationToken cancellationToken)
    {
        using var timeout = Timeout(request, cancellationToken);
        var (config, configFingerprint) = Configuration.Load(Path.GetFullPath(request.ProjectRoot), request.ConfigPath);
        var snapshot = await ProjectDiscovery.DiscoverAsync(request, config, timeout.Token, Definitions);
        var diagnostics = new List<Diagnostic>(snapshot.Diagnostics);
        BuildArtifacts? artifacts = null;
        try
        {
            if (request.Execute)
            {
                await EvidenceStore.EnsureWritableAsync(snapshot.Root, timeout.Token);
                try { artifacts = await new BuildExecutor(adapters.OfType<ILanguageExecution>()).CollectAsync(snapshot, timeout.Token); }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
                { diagnostics.Add(new("execution_failed", ex.Message)); }
                AppendBuildDiagnostics(diagnostics, artifacts);
            }
            var afterBuild = await ProjectDiscovery.DiscoverAsync(request, config, timeout.Token, Definitions);
            if (afterBuild.SourceFingerprint != snapshot.SourceFingerprint)
                throw new InvalidOperationException("Source/build inputs changed during execution. Evidence is not accepted; rerun against a stable snapshot.");
            var facts = await CollectFacts(snapshot, diagnostics, timeout.Token);
            diagnostics.AddRange(facts.Diagnostics);
            CoverageEvidence? coverage = null;
            try
            {
                coverage = await new CoverageCollection(adapters).CollectAsync(new CoverageInput
                {
                    Snapshot = snapshot, ConfigurationFingerprint = configFingerprint, Request = request,
                    Facts = facts, Artifacts = artifacts
                }, timeout.Token);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or System.Xml.XmlException)
            { diagnostics.Add(new("coverage_invalid", "Coverage evidence is invalid: " + ex.Message)); }
            var after = await ProjectDiscovery.DiscoverAsync(request, config, timeout.Token, Definitions);
            if (after.SourceFingerprint != snapshot.SourceFingerprint)
                throw new InvalidOperationException("Source/build inputs changed during analysis. No scores are accepted; rerun against a stable snapshot.");
            if (request.Execute && artifacts is null) coverage = null;
            var results = QualityEngine.Evaluate(new EvaluationRequest
            {
                Snapshot = snapshot, Facts = facts, Config = config, Evidence = coverage,
                ConfigurationFingerprint = configFingerprint, Families = families, CancellationToken = timeout.Token
            });
            timeout.Token.ThrowIfCancellationRequested();
            return new(Wire.SchemaVersion, "2", Wire.Target, snapshot.SourceFingerprint, configFingerprint, request.Scope,
                snapshot.SelectedFiles, snapshot.Modules, results, diagnostics.OrderBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.File, StringComparer.Ordinal).ToArray());
        }
        finally { artifacts?.Dispose(); }
    }

    private static void AppendBuildDiagnostics(List<Diagnostic> diagnostics, BuildArtifacts? artifacts)
    {
        if (artifacts is not null) diagnostics.AddRange(artifacts.Results.SelectMany(result => result.Diagnostics));
    }

    private async Task<AnalysisFacts> CollectFacts(ProjectSnapshot snapshot, List<Diagnostic> diagnostics, CancellationToken token)
    {
        var results = new List<AnalysisFacts>();
        foreach (var adapter in adapters.Where(a => Definitions.Where(d => d.Language == a.Language).Any(d => snapshot.Files.Any(d.IsSource))))
        {
            try
            {
                var facts = await adapter.AnalyzeAsync(snapshot, token);
                if (!facts.SemanticComplete && facts.Types.Length == 0)
                    facts = facts with { Diagnostics = facts.Diagnostics.Append(new Diagnostic("analyzer_failed", "No analyzable types were produced. Check language diagnostics and installed analyzer runtimes.", Language: adapter.Language)).ToArray() };
                results.Add(LanguageIdentity.Qualify(facts, adapter.Language));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException)
            {
                diagnostics.Add(new("analyzer_failed", ex.Message, Language: adapter.Language));
                results.Add(new([], [], [], [new("analyzer_failed", ex.Message, Language: adapter.Language)], false));
            }
        }
        return new(results.SelectMany(r => r.Types).ToArray(), results.SelectMany(r => r.Methods).ToArray(),
            results.SelectMany(r => r.Dependencies).ToArray(), results.SelectMany(r => r.Diagnostics).ToArray(), results.All(r => r.SemanticComplete))
        {
            Contexts = results.SelectMany(r => r.Contexts).ToArray(),
            SyntaxGaps = results.SelectMany(r => r.SyntaxGaps).ToArray()
        };
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
