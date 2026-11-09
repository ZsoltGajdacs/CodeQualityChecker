using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cqc.Core;
using Xunit;

namespace Cqc.Tests;

public sealed class ReportWireTests
{
    [Fact]
    public async Task RealStdioBoundsEscapedPagesAndSurvivesLargeInputErrors()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var root = Directory.CreateTempSubdirectory("cqc-wire-").FullName;
        var expected = Enumerable.Range(0, 1200)
            .Select(i => $"{i:D4}-中文-😀-quoted\"-" + new string('n', 80) + ".java").ToArray();
        try
        {
            foreach (var path in expected) await File.WriteAllTextAsync(Path.Combine(root, path), "", timeout.Token);
            await using var wire = new WireClient(timeout.Token);
            await wire.Request("initialize", new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "cqc-wire-test", version = "1" } });
            await wire.Notify("notifications/initialized");
            var invalid = await wire.Tool("inspect_project", new { projectRoot = root, configPath = new string('x', 500000) });
            Assert.True(invalid.GetProperty("isError").GetBoolean());
            var summary = await wire.Tool("inspect_project", new { projectRoot = root });
            Assert.False(summary.GetProperty("isError").GetBoolean());
            var id = summary.GetProperty("structuredContent").GetProperty("reportId").GetString()!;
            var raw = await Reconstruct(wire, id);
            using var original = JsonDocument.Parse(raw);
            Assert.Equal(expected.Order(StringComparer.Ordinal), original.RootElement.GetProperty("project").GetProperty("files").EnumerateArray().Select(p => p.GetString()));
            var files = await Files(wire, id);
            Assert.Equal(expected.Order(StringComparer.Ordinal), files.Order(StringComparer.Ordinal));
            Assert.Equal(expected.Length, Directory.GetFiles(root).Length);
            Assert.All(Directory.GetFiles(root), path => Assert.Equal(0, new FileInfo(path).Length));
            Assert.False(Directory.Exists(Path.Combine(root, ".cqc")));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task<string> Reconstruct(WireClient wire, string reportId)
    {
        var result = new StringBuilder();
        string? cursor = null;
        var pages = 0;
        do
        {
            var reply = await wire.Tool("get_report_page", new { reportId, section = "raw", cursor });
            Assert.False(reply.GetProperty("isError").GetBoolean());
            var data = reply.GetProperty("structuredContent");
            result.Append(data.GetProperty("rawChunk").GetString());
            cursor = data.GetProperty("nextCursor").GetString();
            pages++;
        } while (cursor is not null);
        Assert.True(pages > 1);
        return result.ToString();
    }

    private static async Task<List<string>> Files(WireClient wire, string reportId)
    {
        var result = new List<string>();
        string? cursor = null;
        do
        {
            var reply = await wire.Tool("get_report_page", new { reportId, section = "files", cursor, limit = 200 });
            var data = reply.GetProperty("structuredContent");
            Assert.Equal(1200, data.GetProperty("total").GetInt32());
            result.AddRange(data.GetProperty("items").EnumerateArray().Select(item => item.GetString()!));
            cursor = data.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        Assert.Equal(result.Count, result.Distinct(StringComparer.Ordinal).Count());
        return result;
    }

    private sealed class WireClient : IAsyncDisposable
    {
        private readonly Process process;
        private readonly CancellationToken token;
        private readonly Task<string> errors;
        private int sequence;

        public WireClient(CancellationToken token)
        {
            this.token = token;
            var server = Environment.GetEnvironmentVariable("CQC_TEST_SERVER");
            var start = new ProcessStartInfo(server ?? "dotnet")
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, WorkingDirectory = McpTests.RepositoryRoot()
            };
            if (server is null) start.ArgumentList.Add(Path.Combine(McpTests.RepositoryRoot(), "src/Cqc.Server/bin/Debug/net10.0/cqc.dll"));
            process = Process.Start(start)!;
            errors = process.StandardError.ReadToEndAsync(token);
        }

        public async Task<JsonElement> Tool(string name, object request)
        {
            var result = await Request("tools/call", new { name, arguments = new { request } });
            Assert.InRange(Encoding.UTF8.GetByteCount(result.GetRawText()), 1, McpReply.MaximumBytes);
            return result;
        }

        public async Task<JsonElement> Request(string method, object parameters)
        {
            var id = ++sequence;
            await Send(new { jsonrpc = "2.0", id, method, @params = parameters });
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(token);
                Assert.NotNull(line);
                Assert.InRange(Encoding.UTF8.GetByteCount(line) + 1, 1, 1024 * 1024 - 1);
                using var response = JsonDocument.Parse(line);
                if (!response.RootElement.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id) continue;
                Assert.False(response.RootElement.TryGetProperty("error", out _), line);
                return response.RootElement.GetProperty("result").Clone();
            }
        }

        public Task Notify(string method) => Send(new { jsonrpc = "2.0", method });
        private async Task Send(object message)
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message, Wire.Json).Replace("\n", ""));
            await process.StandardInput.FlushAsync(token);
        }

        public async ValueTask DisposeAsync()
        {
            process.StandardInput.Close();
            try { await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token); }
            catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); }
            if (!token.IsCancellationRequested) await errors;
            process.Dispose();
        }
    }
}
