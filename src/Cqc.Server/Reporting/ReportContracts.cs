using System.Text.Json;
using System.Text.Json.Serialization;
using Cqc.Core;

namespace Cqc.Server.Reporting;

public static class CompactJson
{
    public static readonly JsonSerializerOptions Options = new(Wire.Json) { WriteIndented = false };
}
public sealed record ReportPageRequest
{
    public required string ReportId { get; init; }
    public required string Section { get; init; }
    public string? Family { get; init; }
    public string? EntityId { get; init; }
    public string? Cursor { get; init; }
    public int Limit { get; init; } = 50;
}
public sealed record DiagnosticCount(string Code, int Count);
public sealed record ScoreCount(double? Score, int Count);
public sealed record FamilySummary(string Family, string Status, int EntityCount, int FindingCount)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ScoreCount[]? Scores { get; init; }
    public ScoreCount[] StaticComplexityScores { get; init; } = [];
}
public sealed record ReportSummary
{
    public required string ReportId { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required string SourceFingerprint { get; init; }
    public string? ConfigurationFingerprint { get; init; }
    public required string Scope { get; init; }
    public required int FileCount { get; init; }
    public required int ModuleCount { get; init; }
    public required FamilySummary[] Families { get; init; }
    public required DiagnosticCount[] Diagnostics { get; init; }
    public required int RemainingDiagnosticGroups { get; init; }
    public required string Kind { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AnalysisNote { get; init; }
    public string SchemaVersion => "3";
    public string Message => Wire.Target;
}
public sealed record ReportPage(string ReportId, string Section, int Total)
{
    public JsonElement[] Items { get; init; } = [];
    public string? NextCursor { get; init; }
    public string? RawChunk { get; init; }
    public string SchemaVersion => "3";
    public string Message => Wire.Target;
}
public sealed record ReportStoreOptions
{
    public TimeSpan Retention { get; init; } = TimeSpan.FromMinutes(60);
    public int MaximumReports { get; init; } = 10;
    public long MaximumBytes { get; init; } = 512L * 1024 * 1024;
}
internal sealed record IndexedRow(string Section, string? Family, string[] Entities, long Offset, int Length);
internal sealed record StoredReport(string Id, string Directory, DateTimeOffset ExpiresAt, long Bytes,
    IndexedRow[] Rows, string[] Families, bool Inspection);
