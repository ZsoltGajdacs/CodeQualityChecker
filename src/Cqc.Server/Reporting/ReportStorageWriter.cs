using System.Text.Json;

namespace Cqc.Server.Reporting;

internal static class ReportStorageWriter
{
    public static async Task<long> MeasureAsync(object report, long maximumBytes, CancellationToken token)
    {
        using var raw = new ReportStorageStream(Stream.Null, maximumBytes);
        await Serialize(report, raw, token);
        using var rows = new ReportStorageStream(Stream.Null, maximumBytes - raw.Length);
        await new ReportIndexWriter(rows, maximumBytes, raw.Length, token).WriteAsync(report);
        return raw.Length + rows.Length;
    }
    public static async Task<IndexedRow[]> WriteAsync(object report, string directory, long reservation, CancellationToken token)
    {
        long rawBytes;
        await using (var file = File.Create(Path.Combine(directory, "raw.json")))
        {
            using var stream = new ReportStorageStream(file, reservation);
            await Serialize(report, stream, token);
            rawBytes = stream.Length;
        }
        await using var rowFile = File.Create(Path.Combine(directory, "rows"));
        using var rowStream = new ReportStorageStream(rowFile, reservation - rawBytes);
        return await new ReportIndexWriter(rowStream, reservation, rawBytes, token).WriteAsync(report);
    }
    private static Task Serialize(object report, Stream stream, CancellationToken token) =>
        JsonSerializer.SerializeAsync(stream, report, report.GetType(), CompactJson.Options, token);
}
