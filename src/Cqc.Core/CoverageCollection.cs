using System.Text.Json;

namespace Cqc.Core;

internal sealed record CoverageInput
{
    public required ProjectSnapshot Snapshot { get; init; }
    public required string ConfigurationFingerprint { get; init; }
    public required AnalysisRequest Request { get; init; }
    public required AnalysisFacts Facts { get; init; }
    public BuildArtifacts? Artifacts { get; init; }
}

/// <summary>Owns coverage provenance, adapter report conversion and atomic evidence publication.</summary>
internal sealed class CoverageCollection(IEnumerable<ILanguageAnalyzer> adapters)
{
    internal async Task<CoverageEvidence?> CollectAsync(CoverageInput input, List<Diagnostic> diagnostics, CancellationToken token)
    {
        var drivers = adapters.OfType<ILanguageExecution>().ToDictionary(x => x.Language, StringComparer.Ordinal);
        if (input.Artifacts is not { } artifacts) return await ReadCachedAsync(input, drivers.Values, diagnostics, token);
        var evidence = BuildEvidence(input, artifacts, drivers);
        await PublishAsync(input.Snapshot.Root, evidence, token);
        return evidence;
    }

    private static async Task<CoverageEvidence?> ReadCachedAsync(CoverageInput input, IEnumerable<ILanguageExecution> drivers,
        List<Diagnostic> diagnostics, CancellationToken token)
    {
        var path = Path.GetFullPath(input.Request.ReportPath ?? ".cqc/coverage.json", input.Snapshot.Root);
        if (!File.Exists(path)) return null;
        var cached = EvidenceStore.Read(await File.ReadAllTextAsync(path, token));
        return MatchIdentities(cached, drivers, diagnostics);
    }

    private static CoverageEvidence BuildEvidence(CoverageInput input, BuildArtifacts artifacts, Dictionary<string, ILanguageExecution> drivers)
    {
        var entries = artifacts.Results.SelectMany(build =>
                drivers[build.Language].ReadCoverage(build, input.Facts.Methods.Where(method => method.Language == build.Language)))
            .GroupBy(method => method.MethodId).Where(group => group.Count() == 1).Select(group => group.Single())
            .OrderBy(method => method.MethodId, StringComparer.Ordinal).ToArray();
        var versions = artifacts.ToolVersions;
        foreach (var build in artifacts.Results)
            if (drivers[build.Language].CoverageIdentityVersion is { } version) versions[IdentityKey(build.Language)] = version;
        return new(Wire.SchemaVersion, input.Snapshot.SourceFingerprint, input.ConfigurationFingerprint, true, versions, entries);
    }

    private static async Task PublishAsync(string root, CoverageEvidence evidence, CancellationToken token)
    {
        EvidenceStore.EnsureArtifactDirectory(root);
        var path = Path.Combine(root, ".cqc", "coverage.json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(evidence, Wire.Json), token);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static CoverageEvidence MatchIdentities(CoverageEvidence evidence, IEnumerable<ILanguageExecution> drivers, List<Diagnostic> diagnostics)
    {
        foreach (var driver in drivers)
        {
            if (driver.CoverageIdentityVersion is not { } version || evidence.ToolVersions.GetValueOrDefault(IdentityKey(driver.Language)) == version)
                continue;
            var methods = evidence.Methods.Where(method => !method.MethodId.StartsWith(driver.Language + ":", StringComparison.Ordinal)).ToArray();
            if (methods.Length == evidence.Methods.Length) continue;
            diagnostics.Add(new("coverage.identity_stale", "Coverage uses an older method identity. Run explicit execution to refresh it.", Language: driver.Language));
            evidence = evidence with { Methods = methods };
        }
        return evidence;
    }

    private static string IdentityKey(string language) => language + ":methodIdentity";
}
