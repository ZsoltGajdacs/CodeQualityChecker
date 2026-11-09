using System.Text.Json;
using Cqc.Core;
using Cqc.Server.Reporting;
using Xunit;

namespace Cqc.Tests;

public sealed class PageBudgetTests
{
    [Fact]
    public async Task EscapedRecordsFillTheSameByteBudgetWithoutLosingRecordsAcrossCursors()
    {
        var diagnostics = Enumerable.Range(0, 400).Select(i => new Diagnostic("detail", i + string.Concat(Enumerable.Repeat("\"\\😀<>&", 150)))).ToArray();
        var report = new InspectionResponse("2", Wire.Target, new("/root", [], [], [], "source", diagnostics), []);
        using var store = new ReportStore(TimeProvider.System);
        var summary = await store.PublishAsync(report, default);
        var request = new ReportPageRequest { ReportId = summary.ReportId, Section = "diagnostics", Limit = 200 };
        var actual = new List<Diagnostic>();
        do
        {
            var page = await store.ReadPageAsync(request, default);
            Assert.NotEmpty(page.Items);
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(page.Items, CompactJson.Options).Length <= 200 * 1024);
            Assert.False(McpReply.Result(page).IsError);
            actual.AddRange(page.Items.Select(item => item.Deserialize<Diagnostic>(CompactJson.Options)!));
            if (page.NextCursor is not null)
            {
                var next = diagnostics[actual.Count];
                var withNext = page.Items.Append(JsonSerializer.SerializeToElement(next, CompactJson.Options));
                Assert.True(JsonSerializer.SerializeToUtf8Bytes(withNext, CompactJson.Options).Length > 200 * 1024);
            }
            request = request with { Cursor = page.NextCursor };
        } while (request.Cursor is not null);
        Assert.Equal(diagnostics, actual);
    }
}
