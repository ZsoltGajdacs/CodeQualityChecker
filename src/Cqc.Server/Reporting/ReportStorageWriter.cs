using System.Text.Json;

namespace Cqc.Server.Reporting;

internal static class ReportStorageWriter
{
    public static async Task<(long Bytes, IndexedRow[] Rows)> WriteAsync(object report, string directory, long maximumBytes, CancellationToken token)
    {
        long rawBytes;
        await using (var file = File.Create(Path.Combine(directory, "raw.json")))
        {
            using var stream = new ReportStorageStream(file, maximumBytes);
            await Serialize(report, stream, token);
            rawBytes = stream.Length;
        }
        await using var rowFile = File.Create(Path.Combine(directory, "rows"));
        using var rowStream = new ReportStorageStream(rowFile, maximumBytes - rawBytes);
        var rows = await new ReportIndexWriter(rowStream, token).WriteAsync(report);
        return (rawBytes + rowStream.Length, rows);
    }
    private static Task Serialize(object report, Stream stream, CancellationToken token) =>
        JsonSerializer.SerializeAsync(stream, report, report.GetType(), CompactJson.Options, token);
}
