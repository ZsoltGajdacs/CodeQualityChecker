using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Cqc.Core;

namespace Cqc.CSharp;

internal sealed record CSharpProjectReferences(string ProjectPath, string[] Paths);
internal sealed record CSharpReferenceManifest(string SchemaVersion, string SourceFingerprint, CSharpProjectReferences[] Projects);

internal static class CSharpReferenceMetadata
{
    internal const string FileName = "csharp-references.json";
    private const string Schema = "1";

    internal static CSharpReferenceManifest? Read(ProjectSnapshot snapshot, List<Diagnostic> diagnostics)
    {
        var directory = Path.Combine(snapshot.Root, ".cqc");
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
        var manifest = JsonSerializer.Deserialize<CSharpReferenceManifest>(File.ReadAllText(path), Wire.Json);
        return manifest is { SchemaVersion: Schema } && manifest.SourceFingerprint == snapshot.SourceFingerprint
            ? ValidateProjects(manifest) : null;
    }

    private static CSharpReferenceManifest ValidateProjects(CSharpReferenceManifest manifest)
    {
        if (!ValidProjects(manifest.Projects))
            throw new JsonException("C# reference manifest must contain one set of absolute reference paths per project.");
        return manifest;
    }

    private static bool ValidProjects(CSharpProjectReferences[]? projects) =>
        projects is not null && projects.All(ValidProject)
        && projects.DistinctBy(project => project.ProjectPath, StringComparer.Ordinal).Count() == projects.Length;

    private static bool ValidProject(CSharpProjectReferences? project) =>
        project is { Paths: not null } && !string.IsNullOrWhiteSpace(project.ProjectPath) && project.Paths.All(AbsolutePath);

    private static bool AbsolutePath(string? path) => path is not null && Path.IsPathFullyQualified(path);

    internal static Task WriteAsync(ProjectSnapshot snapshot, string scratch, IEnumerable<CSharpProjectReferences> projects, CancellationToken token)
    {
        var cache = new ArtifactCache(snapshot);
        var canonical = projects.OrderBy(project => project.ProjectPath, StringComparer.Ordinal)
            .Select(project => Persist(project, scratch, cache, token)).ToArray();
        return cache.ReplaceJsonAsync(FileName, new CSharpReferenceManifest(Schema, snapshot.SourceFingerprint, canonical), token);
    }

    private static CSharpProjectReferences Persist(CSharpProjectReferences project, string scratch, ArtifactCache cache, CancellationToken token)
    {
        var identities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
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
            paths.Add(path.StartsWith(scratch + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? cache.PersistFile(path) : path);
        }
        return project with { Paths = paths.ToArray() };
    }

    private static void Stale(List<Diagnostic> diagnostics) => diagnostics.Add(new("metadata-stale",
        "C# reference evidence is outdated or does not match the source fingerprint. Run explicit execution to refresh it.", Language: "csharp"));
}
