using System.Runtime.InteropServices;
using Cqc.Core;
using Microsoft.CodeAnalysis;
using Diagnostic = Cqc.Core.Diagnostic;

namespace Cqc.CSharp;

/// <summary>Creates project-scoped Roslyn references and shares identical files within one request.</summary>
internal sealed class CachedReferences
{
    private readonly ProjectSnapshot snapshot;
    private readonly List<Diagnostic> manifestDiagnostics = [];
    private readonly Dictionary<string, CSharpReferenceProject> projects;
    private readonly Dictionary<(string Path, string? Sha256), PortableExecutableReference> references = [];
    private readonly Lazy<(string[] Paths, Diagnostic[] Diagnostics)> platform = new(ReadPlatform);

    internal CachedReferences(ProjectSnapshot snapshot)
    {
        this.snapshot = snapshot;
        var manifest = CSharpReferenceMetadata.Read(snapshot, manifestDiagnostics);
        projects = (manifest?.Projects ?? []).ToDictionary(project => project.ProjectPath, StringComparer.Ordinal);
    }

    internal MetadataReference[] Read(ParsedProject source, List<Diagnostic> diagnostics)
    {
        diagnostics.AddRange(manifestDiagnostics);
        var manifest = projects.GetValueOrDefault(source.Path);
        var metadata = manifest is { References.Length: > 0 }
            ? manifest.References.Select(reference => GetReference(reference.Path, reference.Sha256, source, diagnostics))
            : Fallback(source, diagnostics).Select(path => GetReference(path, null, source, diagnostics));
        return metadata.OfType<MetadataReference>().ToArray();
    }

    private PortableExecutableReference? GetReference(string path, string? sha256, ParsedProject source, List<Diagnostic> diagnostics)
    {
        var key = (path, sha256);
        if (references.TryGetValue(key, out var reference)) return reference;
        try
        {
            reference = sha256 is null ? MetadataReference.CreateFromFile(path) : ReadVerified(path, sha256);
            references.Add(key, reference);
            return reference;
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("metadata-invalid", exception.Message, source.Path, "csharp"));
            return null;
        }
    }

    private static PortableExecutableReference ReadVerified(string path, string sha256)
    {
        var bytes = File.ReadAllBytes(path);
        if (Wire.HashBytes(bytes) != sha256) throw new IOException("Reference assembly changed: " + path);
        return MetadataReference.CreateFromImage(ImmutableCollectionsMarshal.AsImmutableArray(bytes), filePath: path);
    }

    private string[] Fallback(ParsedProject source, List<Diagnostic> diagnostics)
    {
        diagnostics.AddRange(platform.Value.Diagnostics);
        if (source.Path == ".") return platform.Value.Paths;
        WarnForDifferentFramework(source, diagnostics);
        var assets = new NuGetProjectAssets(snapshot.Root, source);
        return platform.Value.Paths.Concat(assets.Read(diagnostics)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static void WarnForDifferentFramework(ParsedProject source, List<Diagnostic> diagnostics)
    {
        if (source.Options.TargetFramework is not { } framework) return;
        var host = $"net{Environment.Version.Major}.{Environment.Version.Minor}";
        if (string.Equals(framework, host, StringComparison.OrdinalIgnoreCase)) return;
        diagnostics.Add(new("csharp.framework_reference_unverified",
            $"Host framework metadata is used for target {framework}. Run explicit execution to collect matching references.",
            source.Path, "csharp"));
    }

    private static (string[] Paths, Diagnostic[] Diagnostics) ReadPlatform()
    {
        var diagnostics = new List<Diagnostic>();
        var paths = PlatformReferences.Read(diagnostics);
        return (paths, diagnostics.ToArray());
    }
}
