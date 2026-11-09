using System.Text.Json;
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
public sealed record FamilySummary(string Family, string Status, int EntityCount, int FindingCount, ScoreCount[] Scores);
public sealed record ReportSummary(string ReportId, DateTimeOffset ExpiresAt, string SourceFingerprint,
    string? ConfigurationFingerprint, string Scope, int FileCount, int ModuleCount, FamilySummary[] Families,
    DiagnosticCount[] Diagnostics, int RemainingDiagnosticGroups, string Kind)
{
    public string SchemaVersion => "2";
    public string Message => Wire.Target;
}
public sealed record ReportPage(string ReportId, string Section, int Total)
{
    public JsonElement[] Items { get; init; } = [];
    public string? NextCursor { get; init; }
    public string? RawChunk { get; init; }
    public string SchemaVersion => "2";
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
