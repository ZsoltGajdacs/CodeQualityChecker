using System.Text.Json;
using Cqc.Core;
using ModelContextProtocol.Protocol;

internal static class McpReply
{
    public static async Task<CallToolResult> Invoke<T>(Func<Task<T>> action, CancellationToken token)
    {
        try { return Result(await action(), false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return Result(new { message = Wire.Target, error = "Analysis timed out. No scores were produced; increase timeoutSeconds or reduce scope." }, true); }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException or JsonException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { return Result(new { message = Wire.Target, error = exception.Message }, true); }
    }

    private static CallToolResult Result<T>(T response, bool error)
    {
        var json = JsonSerializer.SerializeToElement(response, Wire.Json);
        return new() { IsError = error, StructuredContent = error ? null : json, Content = [new TextContentBlock { Text = json.GetRawText() }] };
    }
}
