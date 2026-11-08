namespace Cqc.Core;

/// <summary>Registration metadata and manifest interpretation owned by a language module.</summary>
public sealed record LanguageDefinition
{
    public required string Language { get; init; }
    public required string[] SourceExtensions { get; init; }
    public required string[] ManifestPatterns { get; init; }
    public string[] GeneratedSuffixes { get; init; } = [];
    public required Func<string, string, ProjectModule> InspectManifest { get; init; }
    public string[] BuildInputPatterns { get; init; } = [];
    public string[] IgnoredBuildInputPatterns { get; init; } = [];
    public string[] ExecutableBuildInputPatterns { get; init; } = [];
    public string[] BuildTools { get; init; } = [];
    public Func<ProjectSnapshot, Func<string, bool>, IEnumerable<Diagnostic>>? InspectPrerequisites { get; init; }
    public bool IsSource(string file) => SourceExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);
    public bool IsManifest(string file) => ManifestPatterns.Any(pattern => ProjectDiscovery.Glob(Path.GetFileName(file), pattern));
}

public interface ILanguageDiscovery
{
    LanguageDefinition Definition { get; }
}
