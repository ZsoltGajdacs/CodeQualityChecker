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
    internal async Task<CoverageEvidence?> CollectAsync(CoverageInput input, CancellationToken token)
    {
        var snapshot = input.Snapshot;
        var configFingerprint = input.ConfigurationFingerprint;
        var request = input.Request;
        var facts = input.Facts;
        var artifacts = input.Artifacts;
        var path = Path.GetFullPath(artifacts is null ? request.ReportPath ?? ".cqc/coverage.json" : ".cqc/coverage.json", snapshot.Root);
        if (artifacts is null)
        {
            if (!File.Exists(path)) return null;
            return EvidenceStore.Read(await File.ReadAllTextAsync(path, token));
        }
        var drivers = adapters.OfType<ILanguageExecution>().ToDictionary(x => x.Language, StringComparer.Ordinal);
        var entries = artifacts.Results.SelectMany(build => drivers[build.Language].ReadCoverage(build, facts.Methods.Where(m => m.Language == build.Language)))
            .GroupBy(m => m.MethodId).Where(g => g.Count() == 1).Select(g => g.Single()).OrderBy(m => m.MethodId, StringComparer.Ordinal).ToArray();
        var evidence = new CoverageEvidence(Wire.SchemaVersion, snapshot.SourceFingerprint, configFingerprint, true, artifacts.ToolVersions, entries);
        EvidenceStore.EnsureArtifactDirectory(snapshot.Root);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(evidence, Wire.Json), token);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return evidence;
    }

}
