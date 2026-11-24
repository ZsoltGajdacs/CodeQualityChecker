using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cqc.Core;

public static class Wire
{
    public const string SchemaVersion = "2";
    public const string Target = "10 is the desired score.";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string HashBytes(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
}

public sealed record Location(string File, int StartLine, int EndLine);
public sealed record MethodFact(
    string Id, string TypeId, string Language, Location Location, int Complexity,
    int ExecutableLines, bool IsTest, bool IsFixture, bool IsParameterized,
    int Assertions, string[] Calls, string[] Fingerprints, int NodeCount,
    bool Complete = true, string? CoverageSignature = null)
{
    public string? ContextId { get; init; }
    public string? QualifiedName { get; init; }
    public EvidenceState? Evidence { get; init; }
    public int? DuplicationLines { get; init; }
}
public sealed record TypeFact(string Id, string Language, Location Location, bool IsAbstract)
{
    public bool IsTest { get; init; }
    public bool Complete { get; init; } = true;
    public string? ContextId { get; init; }
    public string? QualifiedName { get; init; }
    public EvidenceState? Evidence { get; init; }
}
public sealed record DependencyFact(string From, string To, Location Location);
public sealed record AnalysisFacts(
    TypeFact[] Types, MethodFact[] Methods, DependencyFact[] Dependencies,
    Diagnostic[] Diagnostics, bool SemanticComplete, string SchemaVersion = Wire.SchemaVersion)
{
    public AnalysisContext[] Contexts { get; init; } = [];
    public SyntaxGap[] SyntaxGaps { get; init; } = [];
    public ExecutableFact[] Executables { get; init; } = [];
}
public sealed record ProjectModule(string Language, string Path, string Integration, bool Supported);
public sealed record ProjectSnapshot(
    string Root, string[] Files, string[] SelectedFiles, ProjectModule[] Modules,
    string SourceFingerprint, Diagnostic[] Diagnostics, string SchemaVersion = Wire.SchemaVersion)
{
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> SourceLanguages { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SourceOnly { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReferenceEvidenceDirectory { get; init; }
}

public sealed record AnalysisRequest
{
    public required string ProjectRoot { get; init; }
    [Description("Selection scope: repository (default) checks all source files; paths checks the supplied paths; changed without gitBase checks staged, unstaged, and untracked source files. Checks assess whole selected files using current working-tree content and repository context, so findings may reference unselected counterpart files.")]
    public string Scope { get; init; } = "repository";
    [Description("Project-relative files or directories to select when scope is paths.")]
    public string[] Paths { get; init; } = [];
    [Description("Optional Git commit or branch to compare against when scope is changed. Omit for uncommitted changes only. An explicit base also includes untracked source files. Checks always read current working-tree content, including unstaged edits to staged files.")]
    public string? GitBase { get; init; }
    public bool Execute { get; init; }
    public string? ConfigPath { get; init; }
    public string? ReportPath { get; init; }
    public int TimeoutSeconds { get; init; } = 300;
}

public sealed record QualityConfig
{
    internal const int MinimumDuplicateLines = 4;

    public int ComplexityThreshold { get; init; } = 8;
    public double CrapThreshold { get; init; } = 30;
    public int TestLinesThreshold { get; init; } = 50;
    public double DuplicateSimilarity { get; init; } = .82;
    public int DuplicateMinLines { get; init; } = MinimumDuplicateLines;
    public int DuplicateMinNodes { get; init; } = 20;
    public SortedDictionary<string, string[]> Components { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, string[]> AllowedDependencies { get; init; } = new(StringComparer.Ordinal);
    public string[][] ForbiddenDependencies { get; init; } = [];
    public string[] Exclude { get; init; } = [];
}

public sealed record Finding(string Id, string Rule, string Message, string Advice,
    string[] EntityIds, Location[] Locations, int Deduction = 1);
public sealed record EntityScore(string EntityId, string Kind, string File, string Status,
    double? Score, string[] FindingIds, SortedDictionary<string, double?> Metrics)
{
    public string[] Blockers { get; init; } = [];
    public AssessmentResult? StaticComplexity { get; init; }
}
public sealed record MethodCoverage(string MethodId, int CoveredBranches, int TotalBranches, int CoveredLines, int TotalLines);
public sealed record CoverageEvidence(string SchemaVersion, string SourceFingerprint, string ConfigurationFingerprint,
    bool Successful, SortedDictionary<string, string> ToolVersions, MethodCoverage[] Methods);

public interface ILanguageAnalyzer
{
    string Language { get; }
    Task<AnalysisFacts> AnalyzeAsync(ProjectSnapshot snapshot, CancellationToken cancellationToken);
}
