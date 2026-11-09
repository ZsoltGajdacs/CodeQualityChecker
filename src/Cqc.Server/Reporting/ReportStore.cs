using System.Text.Json;
using Cqc.Core;

namespace Cqc.Server.Reporting;

public sealed class ReportStore : IDisposable
{
    private readonly TimeProvider clock;
    private readonly ReportStoreOptions options;
    private readonly string directory;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly List<StoredReport> reports = [];
    private readonly ReportPager pager = new(new ReportCursor());
    private readonly ITimer timer;
    private bool disposed;
    private string? pendingCleanup;

    public ReportStore(TimeProvider clock, ReportStoreOptions? options = null)
    {
        this.clock = clock;
        this.options = options ?? new();
        ArgumentOutOfRangeException.ThrowIfLessThan(this.options.MaximumReports, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(this.options.MaximumBytes, 1);
        if (this.options.Retention <= TimeSpan.Zero) throw new ArgumentException("Report retention must be positive.");
        directory = Directory.CreateTempSubdirectory("cqc-reports-").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        timer = clock.CreateTimer(_ => Sweep(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }
    public Task<ReportSummary> PublishAsync(AnalysisResponse report, CancellationToken token) => Publish(report, token);
    public Task<ReportSummary> PublishAsync(InspectionResponse report, CancellationToken token, string scope = "repository") => Publish(report, token, scope);

    private async Task<ReportSummary> Publish(object report, CancellationToken token, string scope = "repository")
    {
        await gate.WaitAsync(token);
        string? pending = null;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            RetryPendingCleanup();
            var bytes = await ReportStorageWriter.MeasureAsync(report, options.MaximumBytes, token);
            token.ThrowIfCancellationRequested();
            SweepLocked();
            while (reports.Count >= options.MaximumReports || reports.Sum(r => r.Bytes) + bytes > options.MaximumBytes) Remove(reports[0]);
            var id = Guid.NewGuid().ToString("N");
            pending = Path.Combine(directory, id);
            Directory.CreateDirectory(pending);
            var rows = await ReportStorageWriter.WriteAsync(report, pending, bytes, token);
            token.ThrowIfCancellationRequested();
            var expires = clock.GetUtcNow() + options.Retention;
            var families = report is AnalysisResponse analysis ? analysis.Results.Select(f => f.Family).ToArray() : [];
            var summary = ReportProjection.Summary(report, id, expires, scope);
            reports.Add(new(id, pending, expires, bytes, rows, families, report is InspectionResponse));
            pending = null;
            return summary;
        }
        finally
        {
            try
            {
                if (pending is not null) pendingCleanup = pending;
                RetryPendingCleanup();
            }
            finally { gate.Release(); }
        }
    }
    public async Task<ReportPage> ReadPageAsync(ReportPageRequest request, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            SweepLocked();
            var report = reports.Find(r => r.Id == request.ReportId)
                ?? throw new ArgumentException("Report is unavailable, expired, or evicted; run inspection or analysis again.");
            return await pager.ReadAsync(report, request, token);
        }
        finally { gate.Release(); }
    }
    private void Sweep()
    {
        if (!gate.Wait(0)) return;
        try { if (!disposed) { RetryPendingCleanup(); SweepLocked(); } }
        catch (IOException) { /* Retry temporary cleanup failures on the next sweep. */ }
        catch (UnauthorizedAccessException) { /* Cleanup must never terminate the MCP host. */ }
        finally { gate.Release(); }
    }
    private void RetryPendingCleanup()
    {
        // Failed pending writes still consume disk space. Block publication until their cleanup succeeds.
        if (pendingCleanup is null) return;
        Directory.Delete(pendingCleanup, true);
        pendingCleanup = null;
    }
    private void SweepLocked()
    {
        foreach (var report in reports.Where(r => r.ExpiresAt <= clock.GetUtcNow()).ToArray()) Remove(report);
    }
    private void Remove(StoredReport report)
    {
        Directory.Delete(report.Directory, true);
        reports.Remove(report);
    }
    public void Dispose()
    {
        timer.Dispose();
        gate.Wait();
        try
        {
            if (disposed) return;
            disposed = true;
            Directory.Delete(directory, true);
            reports.Clear();
        }
        finally { gate.Release(); }
    }
}
