using System.Text;
using System.Text.Json;

namespace Cqc.Server.Reporting;

internal sealed class ReportPager(ReportCursor cursors)
{
    public async Task<ReportPage> ReadAsync(StoredReport report, ReportPageRequest request, CancellationToken token)
    {
        ReportPageValidation.Validate(report, request);
        var position = cursors.Decode(request);
        if (request.Section == "raw") return await Raw(report, request, position, token);
        var rows = report.Rows.Where(r => Matches(r, request)).ToArray();
        if (position > rows.Length) throw new ArgumentException("Cursor is outside the section.");
        var items = new List<JsonElement>();
        await using var stream = File.OpenRead(Path.Combine(report.Directory, "rows"));
        var next = (int)position;
        var itemBytes = 2; // JSON array brackets, plus one comma between records.
        while (next < rows.Length && items.Count < request.Limit)
        {
            var row = rows[next];
            var item = await ReadRow(stream, row, next, token);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(item, CompactJson.Options).Length + (items.Count == 0 ? 0 : 1);
            if (itemBytes + bytes > 200 * 1024) break;
            items.Add(item);
            itemBytes += bytes;
            next++;
        }
        return new(report.Id, request.Section, rows.Length) { Items = items.ToArray(), NextCursor = next < rows.Length ? cursors.Encode(request, next) : null };
    }
    private static bool Matches(IndexedRow row, ReportPageRequest request) => row.Section == request.Section
        && (request.Family is null || row.Family == request.Family)
        && (request.EntityId is null || row.Entities.Contains(request.EntityId, StringComparer.Ordinal));
    private static async Task<JsonElement> ReadRow(FileStream stream, IndexedRow row, int index, CancellationToken token)
    {
        if (row.Length > 190 * 1024)
            return JsonSerializer.SerializeToElement(new { oversizedItem = true, index, detailSection = "raw", message = "Retrieve raw chunks to access this complete record." }, CompactJson.Options);
        stream.Position = row.Offset;
        var bytes = new byte[row.Length];
        await stream.ReadExactlyAsync(bytes, token);
        return JsonSerializer.Deserialize<JsonElement>(bytes, CompactJson.Options);
    }
    private async Task<ReportPage> Raw(StoredReport report, ReportPageRequest request, long position, CancellationToken token)
    {
        await using var stream = File.OpenRead(Path.Combine(report.Directory, "raw.json"));
        if (position > stream.Length) throw new ArgumentException("Raw cursor is outside the report.");
        stream.Position = position;
        var bytes = new byte[(int)Math.Min(32 * 1024, stream.Length - position)];
        await stream.ReadExactlyAsync(bytes, token);
        var count = bytes.Length;
        if (position + count < stream.Length) count = CompleteUtf8Prefix(bytes);
        var next = position + count;
        return new(report.Id, request.Section, checked((int)stream.Length)) { NextCursor = next < stream.Length ? cursors.Encode(request, next) : null,
            RawChunk = Encoding.UTF8.GetString(bytes, 0, count) };
    }
    private static int CompleteUtf8Prefix(byte[] bytes)
    {
        var lead = bytes.Length - 1;
        while (lead > 0 && (bytes[lead] & 0xc0) == 0x80) lead--;
        if (bytes[lead] < 0x80) return bytes.Length;
        var required = bytes[lead] < 0xe0 ? 2 : bytes[lead] < 0xf0 ? 3 : 4;
        return bytes.Length - lead == required ? bytes.Length : lead;
    }

}
