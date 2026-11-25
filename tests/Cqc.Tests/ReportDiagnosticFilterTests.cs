using Cqc.Core;
using Cqc.Server.Reporting;
using Xunit;

namespace Cqc.Tests;

public sealed class ReportDiagnosticFilterTests
{
    private const string Rare = "java.coverage_signature_ambiguous";
    private const string Common = "java.unresolved_call";

    [Fact]
    public async Task DiagnosticsFilterReachesRareRecordsAfterLargeSelectedFileSection()
    {
        using var store = new ReportStore(TimeProvider.System);
        var summary = await store.PublishAsync(LargeReport(), CancellationToken.None);
        var request = new ReportPageRequest
        {
            ReportId = summary.ReportId, Section = "diagnostics", DiagnosticCode = Rare, Limit = 1
        };
        var allFamilies = await Read(store, request with { Limit = 10 });
        Assert.Equal(6, allFamilies.Total);
        Assert.Equal("selected", allFamilies.Items[0].GetProperty("scope").GetString());

        request = request with { Family = "code" };
        var first = await Read(store, request);
        Assert.Equal(3, first.Total);
        Assert.Equal("one", Assert.Single(first.Items).GetProperty("message").GetString());
        Assert.NotNull(first.NextCursor);
        var next = request with { Cursor = first.NextCursor, Limit = 2 };
        var second = await Read(store, next);
        Assert.Equal(3, second.Total);
        Assert.Equal(new[] { "two", "three" }, second.Items.Select(item => item.GetProperty("message").GetString()));
        Assert.Null(second.NextCursor);
        await Assert.ThrowsAsync<ArgumentException>(() => Read(store, next with { DiagnosticCode = Common }));
        await Assert.ThrowsAsync<ArgumentException>(() => Read(store, next with { DiagnosticCode = null }));
        await Assert.ThrowsAsync<ArgumentException>(() => Read(store, next with { Family = "tests" }));
    }

    [Theory]
    [InlineData("Java.Coverage_Signature_Ambiguous")]
    [InlineData("unknown")]
    public async Task DiagnosticCodeMatchingIsOrdinalAndMissingCodesReturnEmptyPages(string code)
    {
        using var store = new ReportStore(TimeProvider.System);
        var summary = await store.PublishAsync(Report([Diagnostic(Rare, "known")], []), CancellationToken.None);
        var result = await Read(store, new() { ReportId = summary.ReportId, Section = "diagnostics", DiagnosticCode = code });
        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
        Assert.Null(result.NextCursor);
    }

    private static Task<ReportPage> Read(ReportStore store, ReportPageRequest request) =>
        store.ReadPageAsync(request, CancellationToken.None);

    private static AnalysisResponse LargeReport()
    {
        var diagnostics = Enumerable.Range(0, 39_100)
            .Select(index => new Diagnostic(Common, "Unresolved call.", $"Source{index}.java", "java") { Scope = "selected" })
            .Append(Diagnostic(Rare, "project") with { Scope = "project" }).ToArray();
        var code = new FamilyResult("code", "incomplete")
        {
            Diagnostics =
            [
                Diagnostic(Rare, "one") with { Scope = "selected" }, Diagnostic(Rare, "two"), Diagnostic(Rare, "three"),
                Diagnostic(Common, "similarly named one"), Diagnostic(Common, "similarly named two")
            ]
        };
        var tests = new FamilyResult("tests", "incomplete")
        {
            Diagnostics = [Diagnostic(Rare, "test one"), Diagnostic(Rare, "test two")]
        };
        return Report(diagnostics, [code, tests]);
    }

    private static Diagnostic Diagnostic(string code, string message) => new(code, message, "Context.java", "java") { Scope = "context" };

    private static AnalysisResponse Report(Diagnostic[] diagnostics, FamilyResult[] results) => new()
    {
        SchemaVersion = Wire.SchemaVersion, RulesetVersion = "1", Message = Wire.Target, SourceFingerprint = "source",
        ConfigurationFingerprint = "config", Scope = "paths", Diagnostics = diagnostics, Results = results
    };
}
