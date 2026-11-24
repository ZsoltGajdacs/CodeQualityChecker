using Cqc.Core;
using System.Text.Json;

namespace Cqc.TypeScript;

internal static class TypeScriptWorker
{
    internal static AnalysisFacts ReadFacts(string output, int sourceCount)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected a TypeScript worker facts object.");
        if (!HasExpectedVersion(root) || !HasFactArrays(root))
            throw new JsonException("Unexpected TypeScript worker schema.");
        var facts = root.Deserialize<AnalysisFacts>(Wire.Json);
        if (facts?.Types?.Length != sourceCount)
            throw new JsonException("Expected one TypeScript module fact for each analyzed file.");
        return facts with
        {
            SyntaxGaps = facts.SyntaxGaps.Select(gap => gap.Code == "typescript.parse"
                ? gap with { Message = gap.Construct } : gap).ToArray()
        };
    }

    private static bool HasExpectedVersion(JsonElement root) =>
        root.TryGetProperty("schemaVersion", out var version) && version.ValueKind == JsonValueKind.String
        && version.GetString() == Wire.SchemaVersion;

    private static bool HasFactArrays(JsonElement root) =>
        new[] { "types", "methods", "executables", "dependencies", "contexts", "syntaxGaps", "diagnostics" }
            .All(name => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array);

    internal static IEnumerable<Diagnostic> Prerequisites()
    {
        var worker = Find();
        if (worker.Command is null) yield return new("typescript.worker_missing", worker.Message, Language: "typescript");
    }

    internal static (ProcessCommand? Command, string Message) Find()
    {
        var packaged = Path.Combine(AppContext.BaseDirectory, "typescript-worker");
        if (Directory.Exists(packaged))
            return Locate(packaged, Path.Combine(AppContext.BaseDirectory, "node-runtime", "bin", "node"));

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "typescript-worker");
            if (File.Exists(Path.Combine(candidate, "worker.mjs"))) return Locate(candidate, NodeOnPath());
        }
        return (null, "Install the packaged TypeScript worker or restore its locked development dependencies.");
    }

    private static (ProcessCommand? Command, string Message) Locate(string worker, string? node)
    {
        if (node is null || !File.Exists(node))
            return (null, "Install the packaged Node runtime or use Node 24 on PATH for development.");
        var entry = Path.Combine(worker, "worker.mjs");
        if (!File.Exists(entry) || !File.Exists(Path.Combine(worker, "node_modules", "typescript", "lib", "typescript.js")))
            return (null, "Restore the TypeScript worker with npm ci --ignore-scripts in typescript-worker, or reinstall the package.");
        return (new()
        {
            Executable = node,
            Arguments = [entry],
            Directory = worker,
            RemoveEnvironmentVariables = ["NODE_OPTIONS", "NODE_PATH", "NODE_COMPILE_CACHE", "NODE_V8_COVERAGE"]
        }, "");
    }

    private static string? NodeOnPath() => (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator).Where(Path.IsPathFullyQualified)
        .Select(directory => Path.Combine(directory, OperatingSystem.IsWindows() ? "node.exe" : "node"))
        .FirstOrDefault(File.Exists);
}
