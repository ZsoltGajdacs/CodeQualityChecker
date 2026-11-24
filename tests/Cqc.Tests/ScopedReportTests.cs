using System.Text.Json;
using Cqc.Core;
using Cqc.Server.Reporting;
using Xunit;

namespace Cqc.Tests;

public sealed class ScopedReportTests
{
    [Fact]
    public async Task DiagnosticsPrioritizeSelectedFilesAcrossFamilies()
    {
        using var store = new ReportStore(TimeProvider.System);
        var familyDiagnostic = Gap("Selected.java", "selected") with
        {
            Location = new("Selected.java", 3, 4),
            Construct = "initializer"
        };
        var project = new Diagnostic("unsupported_language", "Files with .kt extension are not analyzed.") { Scope = "project", Extension = ".kt" };
        var family = new FamilyResult("code", "incomplete") { Diagnostics = [familyDiagnostic] };
        var report = Review(family, [Gap("Context.java", "context"), project]);
        var summary = await store.PublishAsync(report, CancellationToken.None);
        var group = Assert.Single(summary.Diagnostics, item => item.Code == familyDiagnostic.Code);
        Assert.Equal(2, group.Count);
        Assert.Equal(1, group.SelectedCount);
        Assert.Equal(1, group.ContextCount);
        var unsupported = Assert.Single(summary.Diagnostics, item => item.Code == project.Code);
        Assert.Equal(1, unsupported.ProjectWideCount);
        Assert.Equal(new[] { ".kt" }, unsupported.Extensions);

        var request = new ReportPageRequest { ReportId = summary.ReportId, Section = "diagnostics", Limit = 1 };
        var first = await store.ReadPageAsync(request, CancellationToken.None);
        var detail = Assert.Single(first.Items);
        Assert.Equal("code", detail.GetProperty("family").GetString());
        Assert.Equal("selected", detail.GetProperty("scope").GetString());
        Assert.Equal(3, detail.GetProperty("location").GetProperty("startLine").GetInt32());
        Assert.Equal("initializer", detail.GetProperty("construct").GetString());
        Assert.NotNull(first.NextCursor);
        var next = await store.ReadPageAsync(request with { Cursor = first.NextCursor, Limit = 2 }, CancellationToken.None);
        Assert.Equal(new[] { "context", "project" }, next.Items.Select(item => item.GetProperty("scope").GetString()));
        Assert.Equal(".kt", next.Items[1].GetProperty("extension").GetString());
        Assert.Null(next.NextCursor);
    }

    [Fact]
    public async Task SourceOnlyReportsKeepStructuralBlockersAndOmitEmptyBlockers()
    {
        using var store = new ReportStore(TimeProvider.System);
        var family = new FamilyResult("code", "incomplete")
        {
            Entities = [Entity("selected", "syntax.incomplete"), Entity("healthy")],
            ScoredFileCount = 1,
            UnscoredFileCount = 1
        };
        var summary = await store.PublishAsync(Review(family, []), CancellationToken.None);
        var code = Assert.Single(summary.Families);
        Assert.Null(code.Scores);
        Assert.Equal(1, code.ScoredFileCount);
        Assert.Equal(1, code.UnscoredFileCount);
        var request = new ReportPageRequest { ReportId = summary.ReportId, Section = "entities", Family = "code" };
        var page = await store.ReadPageAsync(request, CancellationToken.None);
        AssertAssessment(page.Items[0], page.Items[1]);

        var raw = await store.ReadPageAsync(request with { Section = "raw", Family = null }, CancellationToken.None);
        Assert.Null(raw.NextCursor);
        using var document = JsonDocument.Parse(raw.RawChunk!);
        var rawFamily = document.RootElement.GetProperty("results")[0];
        Assert.Equal(1, rawFamily.GetProperty("unscoredFileCount").GetInt32());
        var entities = rawFamily.GetProperty("entities");
        AssertAssessment(entities[0], entities[1]);
    }

    [Fact]
    public async Task EntityFindingPagesIncludeStaticOnlyFindingsAndCountSharedFindingsOnce()
    {
        using var store = new ReportStore(TimeProvider.System);
        var type = new EntityScore("java:Selected", "class", "Selected.java", "complete", 9, ["shared"], new(StringComparer.Ordinal))
        {
            StaticComplexity = new("complete", 8, ["shared", "initializer"], [])
        };
        var file = type with { EntityId = "file:Selected.java", Kind = "file" };
        var family = new FamilyResult("code", "complete")
        {
            Entities = [type, file],
            Findings = [ComplexityFinding("shared", type), ComplexityFinding("initializer", type)]
        };
        var report = Review(family, []) with { SourceOnly = false };
        var summary = await store.PublishAsync(report, CancellationToken.None);
        var request = new ReportPageRequest { ReportId = summary.ReportId, Section = "entities", Family = "code" };
        var entities = await store.ReadPageAsync(request, CancellationToken.None);
        Assert.Equal(2, entities.Items.Length);
        Assert.All(entities.Items, entity => Assert.Equal(2, entity.GetProperty("findingCount").GetInt32()));
        foreach (var entity in family.Entities)
        {
            var page = await store.ReadPageAsync(request with { Section = "findings", EntityId = entity.EntityId }, CancellationToken.None);
            Assert.Equal(new[] { "shared", "initializer" }, page.Items.Select(item => item.GetProperty("id").GetString()));
        }
    }

    private static Finding ComplexityFinding(string id, EntityScore entity) =>
        new(id, "code.complexity", "Complex body", "Extract logic", [entity.EntityId], [new(entity.File, 1, 2)]);

    private static void AssertAssessment(JsonElement incomplete, JsonElement complete)
    {
        var blocked = incomplete.GetProperty("staticComplexity");
        Assert.Equal("syntax.incomplete", blocked.GetProperty("blockers")[0].GetString());
        Assert.False(blocked.TryGetProperty("score", out _));
        var scored = complete.GetProperty("staticComplexity");
        Assert.False(scored.TryGetProperty("blockers", out _));
        Assert.Equal(10, scored.GetProperty("score").GetInt32());
    }

    private static EntityScore Entity(string id, params string[] blockers)
    {
        var status = blockers.Length == 0 ? "complete" : "incomplete";
        return new(id, "file", id + ".java", status, null, [], new(StringComparer.Ordinal))
        {
            StaticComplexity = new(status, blockers.Length == 0 ? 10 : null, [], blockers)
        };
    }

    private static AnalysisResponse Review(FamilyResult family, Diagnostic[] diagnostics) =>
        new()
        {
            SchemaVersion = "1", RulesetVersion = "2", Message = Wire.Target, SourceFingerprint = "source",
            ConfigurationFingerprint = "config", Scope = "changed", AnalyzedFiles = ["Selected.java"],
            Results = [family], Diagnostics = diagnostics, SourceOnly = true
        };

    private static Diagnostic Gap(string file, string scope) => new("syntax.parse", "Could not parse source.", file, "java") { Scope = scope };
}
