using Cqc.Core;

namespace Cqc.Server.Reporting;

public sealed class ReportStore : IDisposable
{
    private readonly TimeProvider clock;
    private readonly ReportStoreOptions options;
    private readonly string directory;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly List<StoredReport> reports = [];
    private readonly Queue<string> pendingCleanup = new();
    private readonly ReportPager pager = new(new ReportCursor());
    private readonly ITimer timer;
    private bool disposed;

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
            token.ThrowIfCancellationRequested();
            var id = Guid.NewGuid().ToString("N");
            pending = Path.Combine(directory, id);
            Directory.CreateDirectory(pending);
            var (bytes, rows) = await ReportStorageWriter.WriteAsync(report, pending, options.MaximumBytes, token);
            var now = clock.GetUtcNow();
            var expires = now + options.Retention;
            var families = report is AnalysisResponse analysis ? analysis.Results.Select(f => f.Family).ToArray() : [];
            var summary = ReportProjection.Summary(report, id, expires, scope);
            token.ThrowIfCancellationRequested();
            Commit(new(id, pending, expires, bytes, rows, families, report is InspectionResponse), now);
            pending = null;
            TryPendingCleanup();
            return summary;
        }
        finally
        {
            try
            {
                if (pending is not null)
                {
                    pendingCleanup.Enqueue(pending);
                    TryPendingCleanup();
                }
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
            RetireExpired(clock.GetUtcNow());
            var report = reports.Find(r => r.Id == request.ReportId)
                ?? throw new ArgumentException("Report is unavailable, expired, or evicted; run inspection or analysis again.");
            return await pager.ReadAsync(report, request, token);
        }
        finally { gate.Release(); }
    }
    private void Sweep()
    {
        if (!gate.Wait(0)) return;
        try
        {
            if (disposed) return;
            RetireExpired(clock.GetUtcNow());
            TryPendingCleanup();
        }
        finally { gate.Release(); }
    }

    private void Commit(StoredReport replacement, DateTimeOffset now)
    {
        RetireExpired(now);
        var remainingBytes = reports.Sum(report => report.Bytes);
        while (reports.Count >= options.MaximumReports || remainingBytes > options.MaximumBytes - replacement.Bytes)
        {
            var oldest = reports[0];
            remainingBytes -= oldest.Bytes;
            Retire(oldest);
        }
        reports.Add(replacement);
    }

    private void TryPendingCleanup()
    {
        try { RetryPendingCleanup(); }
        catch (IOException) { /* Retry on the next sweep or publication. */ }
        catch (UnauthorizedAccessException) { /* Retry on the next sweep or publication. */ }
    }

    private void RetryPendingCleanup()
    {
        // Retired and partial reports still use disk space until deletion succeeds.
        while (pendingCleanup.TryPeek(out var path))
        {
            try { Directory.Delete(path, true); }
            catch (DirectoryNotFoundException) { }
            pendingCleanup.Dequeue();
        }
    }
    private void RetireExpired(DateTimeOffset now)
    {
        foreach (var report in reports.Where(r => r.ExpiresAt <= now).ToArray()) Retire(report);
    }
    private void Retire(StoredReport report)
    {
        reports.Remove(report);
        pendingCleanup.Enqueue(report.Directory);
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
            pendingCleanup.Clear();
        }
        finally { gate.Release(); }
    }
}
