using System.Text.Json;
using Cqc.Core;
using Cqc.Server.Reporting;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

public static class McpReply
{
    public const int MaximumBytes = 512 * 1024;

    public static async Task<CallToolResult> Invoke<T>(Func<Task<T>> action, CancellationToken token)
    {
        try { return Result(await action()); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return Error("Analysis timed out. No scores were produced; increase timeoutSeconds or reduce scope."); }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException or JsonException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { return Error(exception.Message); }
    }

    public static CallToolResult Result<T>(T response)
    {
        var text = response switch
        {
            ReportSummary summary => $"{Wire.Target} Report {summary.ReportId}; use get_report_page for details.",
            ReportPage => "Report page. Follow nextCursor until null; raw pages reconstruct the complete report.",
            _ => Wire.Target
        };
        var result = new CallToolResult
        {
            IsError = false, StructuredContent = JsonSerializer.SerializeToElement(response, CompactJson.Options),
            Content = [new TextContentBlock { Text = text }]
        };
        return SerializedBytes(result) <= MaximumBytes ? result : Error("Response exceeds the output budget. Retrieve smaller pages or the raw section.");
    }

    public static int SerializedBytes(CallToolResult result)
        => JsonSerializer.SerializeToUtf8Bytes(result, McpJsonUtilities.DefaultOptions).Length;

    public static CallToolResult Error(string message)
    {
        // Exception messages may contain arbitrary project-controlled content.
        var bounded = message.Length <= 4096 ? message : message[..4096] + " [message shortened]";
        return new() { IsError = true, Content = [new TextContentBlock { Text = bounded }] };
    }
}
