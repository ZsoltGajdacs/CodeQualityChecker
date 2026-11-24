using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Cqc.Core;

namespace Cqc.CSharp;

internal sealed record CSharpProjectReferences(string ProjectPath, string[] Paths);
internal sealed record CSharpReference(string Path, string Sha256);
internal sealed record CSharpReferenceProject(string ProjectPath, CSharpReference[] References);
internal sealed record CSharpReferenceManifest(string SchemaVersion, string SourceFingerprint, CSharpReferenceProject[] Projects);

internal static class CSharpReferenceMetadata
{
    internal const string FileName = "csharp-references.json";
    private const string Schema = "2";

    internal static CSharpReferenceManifest? Read(ProjectSnapshot snapshot, List<Diagnostic> diagnostics)
    {
        var directory = snapshot.ReferenceEvidenceDirectory ?? Path.Combine(snapshot.Root, ".cqc");
        var path = Path.Combine(directory, FileName);
        try
        {
            if (!File.Exists(path))
            {
                if (File.Exists(Path.Combine(directory, "csharp-references.txt"))) Stale(diagnostics);
                return null;
            }

            var manifest = ReadMatching(path, snapshot);
            if (manifest is null) Stale(diagnostics);
            return manifest;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("metadata-unreadable", exception.Message, Language: "csharp"));
            return null;
        }
    }

    private static CSharpReferenceManifest? ReadMatching(string path, ProjectSnapshot snapshot)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Expected a C# reference manifest object.");
        if (!document.RootElement.TryGetProperty("schemaVersion", out var version)) return null;
        if (version.ValueKind != JsonValueKind.String || version.GetString() != Schema) return null;
        var manifest = document.RootElement.Deserialize<CSharpReferenceManifest>(Wire.Json)!;
        return manifest.SourceFingerprint == snapshot.SourceFingerprint ? ValidateProjects(manifest) : null;
    }

    private static CSharpReferenceManifest ValidateProjects(CSharpReferenceManifest manifest)
    {
        if (!ValidProjects(manifest.Projects))
            throw new JsonException("C# reference manifest must contain one set of hashed absolute references per project.");
        return manifest;
    }

    private static bool ValidProjects(CSharpReferenceProject[]? projects) =>
        projects is not null && projects.All(ValidProject)
        && projects.DistinctBy(project => project.ProjectPath, StringComparer.Ordinal).Count() == projects.Length;

    private static bool ValidProject(CSharpReferenceProject? project) =>
        project is { References: not null } && !string.IsNullOrWhiteSpace(project.ProjectPath) && project.References.All(ValidReference);

    private static bool ValidReference(CSharpReference? reference) =>
        reference is { Path: not null, Sha256.Length: 64 } && Path.IsPathFullyQualified(reference.Path)
        && reference.Sha256.All(char.IsAsciiHexDigitLower);

    internal static Task WriteAsync(ProjectSnapshot snapshot, string directory, IEnumerable<CSharpProjectReferences> projects, CancellationToken token)
    {
        var canonical = projects.OrderBy(project => project.ProjectPath, StringComparer.Ordinal)
            .Select(project => Describe(project, token)).ToArray();
        return EvidenceStore.WriteJsonAsync(new DirectoryInfo(directory), FileName,
            new CSharpReferenceManifest(Schema, snapshot.SourceFingerprint, canonical), token);
    }

    private static CSharpReferenceProject Describe(CSharpProjectReferences project, CancellationToken token)
    {
        var identities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var references = new List<CSharpReference>();
        foreach (var path in project.Paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            var name = AssemblyName.GetAssemblyName(path).Name ?? Path.GetFileName(path);
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (identities.TryGetValue(name, out var prior))
            {
                if (prior != hash) throw new InvalidOperationException(project.ProjectPath + " resolves conflicting assemblies named " + name + ".");
                continue;
            }
            identities.Add(name, hash);
            references.Add(new(path, hash));
        }
        return new(project.ProjectPath, references.ToArray());
    }

    private static void Stale(List<Diagnostic> diagnostics) => diagnostics.Add(new("metadata-stale",
        "C# reference evidence is outdated or does not match the source fingerprint. Run explicit execution to refresh it.", Language: "csharp"));
}
