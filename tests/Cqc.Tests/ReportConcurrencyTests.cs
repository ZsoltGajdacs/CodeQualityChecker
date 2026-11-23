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

    [Theory]
    [InlineData(1, 120000)]
    [InlineData(10, 60000)]
    public async Task CancellationAtCapacityPreservesEarlierReport(int maximumReports, long maximumBytes)
    {
        var clock = new PublicationClock();
        using var store = new ReportStore(clock, new() { MaximumReports = maximumReports, MaximumBytes = maximumBytes });
        var original = await store.PublishAsync(Inspection(new string('x', 20000)), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        clock.BeforeNextRead = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.PublishAsync(Inspection(new string('y', 20000)), cancellation.Token));

        Assert.NotNull((await Read(store, original.ReportId)).RawChunk);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public async Task StagedIndexWriteFailurePreservesEarlierReportAndAllowsReplacement(int maximumReports)
    {
        using var store = new ReportStore(TimeProvider.System, new() { MaximumReports = maximumReports, MaximumBytes = 60000 });
        var original = await store.PublishAsync(Inspection(new string('x', 20000)), CancellationToken.None);

        // The 40K message fits in raw.json, but writing it again to the index exceeds the stage limit.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PublishAsync(Inspection(new string('y', 40000)), CancellationToken.None));
        Assert.NotNull((await Read(store, original.ReportId)).RawChunk);

        var replacement = await store.PublishAsync(Inspection(new string('z', 20000)), CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => Read(store, original.ReportId));
        Assert.NotNull((await Read(store, replacement.ReportId)).RawChunk);
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

    private static Task<ReportPage> Read(ReportStore store, string id) =>
        store.ReadPageAsync(new() { ReportId = id, Section = "raw" }, CancellationToken.None);

    private sealed class PublicationClock : TimeProvider
    {
        public Action? BeforeNextRead { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            var action = BeforeNextRead;
            BeforeNextRead = null;
            action?.Invoke();
            return DateTimeOffset.UnixEpoch;
        }
    }
}
