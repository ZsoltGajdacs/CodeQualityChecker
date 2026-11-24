using System.Text.Json;
using Cqc.Core;

namespace Cqc.Java;

internal static class JavaWorkerFacts
{
    public static AnalysisFacts Read(string output, string expectedVersion)
    {
        using var wire = JsonDocument.Parse(output);
        var root = wire.RootElement;
        if (!root.TryGetProperty("schemaVersion", out var version) || version.GetString() != expectedVersion)
            return ProtocolError($"Java worker schemaVersion must be {expectedVersion}.");
        if (!root.TryGetProperty("executables", out var executables) || executables.ValueKind != JsonValueKind.Array)
            return ProtocolError("Java analyzer did not return structural executable facts.");

        var facts = JsonSerializer.Deserialize<AnalysisFacts>(output, Wire.Json);
        return facts is null ? Unavailable("java.worker_empty", "Java analyzer returned no facts.") : Validate(facts);
    }

    private static AnalysisFacts Validate(AnalysisFacts facts)
    {
        if (facts.Types.Any(type => type.Evidence is null) || facts.Methods.Any(method => method.Evidence is null))
            return ProtocolError("Java facts require explicit evidence.");
        if (facts.Methods.Any(method => method.DuplicationLines is null or < 0) ||
            facts.Executables.Any(executable => executable.DuplicationLines is null or < 0))
            return ProtocolError("Java facts require duplication line counts.");
        return facts;
    }

    private static AnalysisFacts ProtocolError(string message) =>
        Unavailable("java.worker_protocol", message + " Install matching server and worker artifacts.");

    public static AnalysisFacts Unavailable(string code, string message) =>
        new([], [], [], [new(code, message, Language: "java")], false);
}
