using Cqc.Core;
using Cqc.Server.Reporting;
using Xunit;

namespace Cqc.Tests;

public sealed class ReportConcurrencyTests
{
    [Fact]
    public async Task PhysicalStorageCapEvictsBeforeCountLimitAndFailedPublishPreservesExistingReport()
    {
        using var store = new ReportStore(TimeProvider.System, new() { MaximumReports = 10, MaximumBytes = 60000 });
        var report = Inspection(new string('x', 20000));
        var first = await store.PublishAsync(report, CancellationToken.None);
        var second = await store.PublishAsync(report, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => Read(first.ReportId));
        Assert.NotNull((await Read(second.ReportId)).RawChunk);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PublishAsync(Inspection(new string('x', 40000)), CancellationToken.None));
        Assert.NotNull((await Read(second.ReportId)).RawChunk);
        Task<ReportPage> Read(string id) => store.ReadPageAsync(new() { ReportId = id, Section = "raw" }, CancellationToken.None);
    }

    [Fact]
    public async Task DisposalMakesPublishedReportsUnavailable()
    {
        var store = new ReportStore(TimeProvider.System);
        var summary = await store.PublishAsync(Inspection("detail"), CancellationToken.None);
        store.Dispose();
        store.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.ReadPageAsync(new() { ReportId = summary.ReportId, Section = "raw" }, CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.PublishAsync(Inspection("detail"), CancellationToken.None));
    }

    private static InspectionResponse Inspection(string detail) => new("1", Wire.Target,
        new("/project", [], [], [], "source", [new("diagnostic", detail)]), []);
}
