using System.Text.Json.Serialization;

namespace Cqc.Core;

public sealed record Diagnostic(string Code, string Message, string? File = null, string? Language = null)
{
    [JsonIgnore]
    public bool IsAnalyzerFailure { get; init; }

    public Location? Location { get; init; }
    public string? Construct { get; init; }
    public string? Scope { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Extension { get; init; }
}

public sealed record FamilyResult(string Family, string Status)
{
    public EntityScore[] Entities { get; init; } = [];
    public Finding[] Findings { get; init; } = [];
    public Diagnostic[] Diagnostics { get; init; } = [];
    public int? ScoredFileCount { get; init; }
    public int? UnscoredFileCount { get; init; }
}
public sealed record AnalysisResponse
{
    public required string SchemaVersion { get; init; }
    public required string RulesetVersion { get; init; }
    public required string Message { get; init; }
    public required string SourceFingerprint { get; init; }
    public required string ConfigurationFingerprint { get; init; }
    public required string Scope { get; init; }
    public string[] AnalyzedFiles { get; init; } = [];
    public ProjectModule[] Modules { get; init; } = [];
    public FamilyResult[] Results { get; init; } = [];
    public Diagnostic[] Diagnostics { get; init; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SourceOnly { get; init; }
}

public sealed record InspectionResponse(string SchemaVersion, string Message, ProjectSnapshot Project, Diagnostic[] Prerequisites);
