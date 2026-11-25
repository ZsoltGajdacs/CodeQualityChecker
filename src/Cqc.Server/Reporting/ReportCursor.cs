using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cqc.Server.Reporting;

internal sealed class ReportCursor
{
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    public string Encode(ReportPageRequest request, long position)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(ForRequest(request, position));
        return Convert.ToBase64String(payload) + "." + Convert.ToBase64String(HMACSHA256.HashData(key, payload));
    }
    public long Decode(ReportPageRequest request)
    {
        if (request.Cursor is null) return 0;
        try
        {
            var parts = request.Cursor.Split('.');
            var payload = Convert.FromBase64String(parts[0]);
            if (parts.Length != 2 || !CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, payload), Convert.FromBase64String(parts[1])))
                throw new FormatException();
            var cursor = JsonSerializer.Deserialize<Cursor>(payload)!;
            if (cursor != ForRequest(request, cursor.Position) || cursor.Position < 0)
                throw new FormatException();
            return cursor.Position;
        }
        catch (Exception e) when (e is FormatException or JsonException or IndexOutOfRangeException)
        { throw new ArgumentException("Invalid cursor or cursor does not match report filters."); }
    }

    private static Cursor ForRequest(ReportPageRequest request, long position) =>
        new(request.ReportId, request.IndexSection, request.EntityId, position);

    private sealed record Cursor(string ReportId, ReportSection Section, string? EntityId, long Position);
}
