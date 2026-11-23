using System.Text;
using System.Text.Json;
using Cqc.Core;
using Cqc.Server.Reporting;
using Xunit;

namespace Cqc.Tests;

public sealed class ReportDeliveryTests
{
    [Fact]
    public async Task StaticAssessmentAndBlockersSurviveSummaryPagesAndRawReconstruction()
    {
        using var store = new ReportStore(TimeProvider.System);
        var original = Report(1);
        var family = original.Results.Single();
        var entity = family.Entities.Single() with { Score = null, Status = "incomplete", Blockers = ["coverage.missing"],
            StaticComplexity = new("complete", 9, ["finding"], []) };
        original = original with { Results = [family with { Entities = [entity] }] };
        var summary = await store.PublishAsync(original, CancellationToken.None);
        Assert.Equal(9, Assert.Single(summary.Families.Single().StaticComplexityScores).Score);
        var scores = summary.Families.Single().Scores;
        Assert.NotNull(scores);
        Assert.Null(Assert.Single(scores).Score);
        var page = await store.ReadPageAsync(new() { ReportId = summary.ReportId, Section = "entities", Family = "code" }, CancellationToken.None);
        var item = Assert.Single(page.Items);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("score").ValueKind);
        Assert.Equal("coverage.missing", item.GetProperty("blockers")[0].GetString());
        Assert.Equal(9, item.GetProperty("staticComplexity").GetProperty("score").GetDouble());
        Assert.Equal(JsonSerializer.Serialize(original, CompactJson.Options), await Raw(store, summary.ReportId));
    }

    [Fact]
    public async Task SummaryAndPagesPreserveScoresFiltersAndEveryRecord()
    {
        using var store = new ReportStore(TimeProvider.System);
        var original = Report(123);
        var summary = await store.PublishAsync(original, CancellationToken.None);
        Assert.Equal("3", summary.SchemaVersion);
        Assert.Equal(123, summary.Families.Single().EntityCount);
        var scores = summary.Families.Single().Scores;
        Assert.NotNull(scores);
        Assert.Contains(scores, s => s.Score is null && s.Count > 0);
        Assert.DoesNotContain("results", JsonSerializer.Serialize(summary, CompactJson.Options));
        var items = await All(store, new() { ReportId = summary.ReportId, Section = "entities", Family = "code", Limit = 7 });
        Assert.Equal(123, items.Count);
        Assert.Equal(original.Results[0].Entities.Select(e => e.EntityId), items.Select(e => e.GetProperty("entityId").GetString()));
        Assert.All(items, e => Assert.False(e.TryGetProperty("metrics", out _)));
        var metrics = await All(store, new() { ReportId = summary.ReportId, Section = "metrics", Family = "code", EntityId = "type:3" });
        Assert.Single(metrics);
        Assert.Equal(3, metrics[0].GetProperty("value").GetDouble());
        var empty = await store.ReadPageAsync(new() { ReportId = summary.ReportId, Section = "entities", EntityId = "missing" }, CancellationToken.None);
        Assert.Equal(0, empty.Total);
        Assert.Empty(empty.Items);
        Assert.Null(empty.NextCursor);
    }

    [Fact]
    public async Task RawReconstructionPreservesEscapingUnicodeAndOversizedRecords()
    {
        using var store = new ReportStore(TimeProvider.System);
        var original = Report(5) with
        {
            Diagnostics = [new("large", string.Concat(Enumerable.Repeat("quotation\" \\ \n 中文 😀", 40000)))]
        };
        var summary = await store.PublishAsync(original, CancellationToken.None);
        var page = await store.ReadPageAsync(new() { ReportId = summary.ReportId, Section = "diagnostics" }, CancellationToken.None);
        Assert.True(page.Items[0].GetProperty("oversizedItem").GetBoolean());
        Assert.Null(page.NextCursor);
        var actual = await Raw(store, summary.ReportId);
        Assert.Equal(JsonSerializer.Serialize(original, CompactJson.Options), actual);
        Assert.InRange(McpReply.SerializedBytes(McpReply.Result(summary)), 1, McpReply.MaximumBytes);
    }

    [Fact]
    public async Task CursorsAreBoundToReportSectionAndFiltersButAllowChangingLimit()
    {
        using var store = new ReportStore(TimeProvider.System);
        var summary = await store.PublishAsync(Report(12), CancellationToken.None);
        var request = new ReportPageRequest { ReportId = summary.ReportId, Section = "entities", Family = "code", Limit = 2 };
        var first = await store.ReadPageAsync(request, CancellationToken.None);
        var second = await store.ReadPageAsync(request with { Cursor = first.NextCursor, Limit = 7 }, CancellationToken.None);
        Assert.Equal("type:2", second.Items[0].GetProperty("entityId").GetString());
        Assert.Equal(7, second.Items.Length);
        await Invalid(request with { Cursor = first.NextCursor, Section = "metrics" });
        await Invalid(request with { Cursor = first.NextCursor, EntityId = "type:2" });
        await Invalid(request with { Cursor = "not-a-cursor" });
        await Invalid(request with { Family = "missing" });
        await Invalid(request with { Section = "files", Family = "code" });
        await Invalid(request with { Limit = 201 });
        await Invalid(request with { Limit = 0 });
        var another = await store.PublishAsync(Report(1), CancellationToken.None);
        await Invalid(request with { ReportId = another.ReportId, Cursor = first.NextCursor });
        async Task Invalid(ReportPageRequest invalid) => await Assert.ThrowsAsync<ArgumentException>(() => store.ReadPageAsync(invalid, CancellationToken.None));
    }

    [Fact]
    public async Task StoreExpiresEvictsAndRejectsCanceledOrOversizedPublications()
    {
        var clock = new TestClock();
        using var store = new ReportStore(clock, new() { MaximumReports = 1, MaximumBytes = 50000 });
        var first = await store.PublishAsync(Report(1), CancellationToken.None);
        var second = await store.PublishAsync(Report(2), CancellationToken.None);
        await Unavailable(first.ReportId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.PublishAsync(Report(3), cancellation.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PublishAsync(Report(1) with { Diagnostics = [new("huge", new string('x', 60000))] }, CancellationToken.None));
        Assert.NotEmpty(await Raw(store, second.ReportId));
        clock.Now += TimeSpan.FromMinutes(60);
        await Unavailable(second.ReportId);
        var afterExpiry = await store.PublishAsync(Report(1), CancellationToken.None);
        Assert.NotEmpty(await Raw(store, afterExpiry.ReportId));
        async Task Unavailable(string id) => await Assert.ThrowsAsync<ArgumentException>(() => store.ReadPageAsync(new() { ReportId = id, Section = "raw" }, CancellationToken.None));
    }

    [Fact]
    public async Task InflightCancellationDoesNotPreventLaterPublicationOrRetrieval()
    {
        using var store = new ReportStore(TimeProvider.System);
        var original = await store.PublishAsync(Report(1), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var pending = store.PublishAsync(Report(12000), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.NotEmpty(await Raw(store, original.ReportId));
        var next = await store.PublishAsync(Report(2), CancellationToken.None);
        Assert.NotEmpty(await Raw(store, next.ReportId));
    }

    [Fact]
    public async Task FindingsCanBeRetrievedForListedFileEntities()
    {
        using var store = new ReportStore(TimeProvider.System);
        var finding = new Finding("finding1", "code.complexity", "Simplify", "Extract", ["type:0"], [new("a.cs", 1, 2)]);
        var entity = new EntityScore("file:a.cs", "file", "a.cs", "complete", 9, [finding.Id], new(StringComparer.Ordinal));
        var report = Report(0) with { Results = [new("code", "complete", [entity], [finding], [])] };
        var summary = await store.PublishAsync(report, CancellationToken.None);
        var findings = await All(store, new() { ReportId = summary.ReportId, Section = "findings", Family = "code", EntityId = entity.EntityId });
        Assert.Single(findings);
        Assert.Equal(finding.Id, findings[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task ConcurrentReadersReconstructTheSameSnapshot()
    {
        using var store = new ReportStore(TimeProvider.System);
        var report = Report(200);
        var summary = await store.PublishAsync(report, CancellationToken.None);
        var reads = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Raw(store, summary.ReportId)));
        Assert.All(reads, raw => Assert.Equal(JsonSerializer.Serialize(report, CompactJson.Options), raw));
    }

    [Fact]
    public async Task InspectionRetainsPrerequisitesAndLimitsDiagnosticGroups()
    {
        using var store = new ReportStore(TimeProvider.System);
        var report = new InspectionResponse("1", Wire.Target,
            new("/project", ["a.java", "b.java"], ["a.java"], [], "source", Enumerable.Range(0, 25).Select(i => new Diagnostic("code" + i, "detail")).ToArray()),
            [new("missing_build_tool", "Install Java")]);
        var summary = await store.PublishAsync(report, CancellationToken.None, "paths");
        Assert.Equal("paths", summary.Scope);
        Assert.Equal(1, summary.FileCount);
        Assert.Single(await All(store, new() { ReportId = summary.ReportId, Section = "files" }));
        Assert.Equal(20, summary.Diagnostics.Length);
        Assert.Equal(6, summary.RemainingDiagnosticGroups);
        var prerequisites = await All(store, new() { ReportId = summary.ReportId, Section = "prerequisites" });
        Assert.Single(prerequisites);
        Assert.Equal("missing_build_tool", prerequisites[0].GetProperty("code").GetString());
        Assert.Equal(JsonSerializer.Serialize(report, CompactJson.Options), await Raw(store, summary.ReportId));
    }

    [Fact]
    public void FinalGateBoundsArbitraryErrorsAndOversizedPayloads()
    {
        var error = McpReply.Error(string.Concat(Enumerable.Repeat("😀\"\\", 500000)));
        Assert.True(error.IsError);
        Assert.InRange(McpReply.SerializedBytes(error), 1, McpReply.MaximumBytes);
        var oversized = McpReply.Result(new { value = new string('x', McpReply.MaximumBytes) });
        Assert.True(oversized.IsError);
        Assert.InRange(McpReply.SerializedBytes(oversized), 1, McpReply.MaximumBytes);
    }

    private static async Task<List<JsonElement>> All(ReportStore store, ReportPageRequest request)
    {
        var items = new List<JsonElement>();
        do
        {
            var page = await store.ReadPageAsync(request, CancellationToken.None);
            Assert.InRange(McpReply.SerializedBytes(McpReply.Result(page)), 1, McpReply.MaximumBytes);
            items.AddRange(page.Items);
            request = request with { Cursor = page.NextCursor };
        } while (request.Cursor is not null);
        return items;
    }

    private static async Task<string> Raw(ReportStore store, string id)
    {
        var raw = new StringBuilder();
        var request = new ReportPageRequest { ReportId = id, Section = "raw" };
        do
        {
            var page = await store.ReadPageAsync(request, CancellationToken.None);
            Assert.InRange(McpReply.SerializedBytes(McpReply.Result(page)), 1, McpReply.MaximumBytes);
            raw.Append(page.RawChunk);
            request = request with { Cursor = page.NextCursor };
        } while (request.Cursor is not null);
        return raw.ToString();
    }

    private static AnalysisResponse Report(int count)
    {
        var entities = Enumerable.Range(0, count).Select(i => new EntityScore("type:" + i, "class", "a.cs", "incomplete", i % 2 == 0 ? null : 9,
            [], new(StringComparer.Ordinal) { ["complexity"] = i })).ToArray();
        return new("1", "1", Wire.Target, "source", "configuration", "paths", ["a.cs"], [],
            [new("code", "incomplete", entities, [], [])], []);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
