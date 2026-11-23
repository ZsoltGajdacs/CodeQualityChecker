using Cqc.Core;
using Microsoft.CodeAnalysis;
using Diagnostic = Cqc.Core.Diagnostic;

namespace Cqc.CSharp;

/// <summary>Creates project-scoped Roslyn references and shares identical files within one request.</summary>
internal sealed class CachedReferences
{
    private readonly ProjectSnapshot snapshot;
    private readonly List<Diagnostic> manifestDiagnostics = [];
    private readonly Dictionary<string, CSharpProjectReferences> projects;
    private readonly Dictionary<string, PortableExecutableReference> references = new(StringComparer.Ordinal);
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
        var paths = manifest is { Paths.Length: > 0 } ? manifest.Paths : Fallback(source, diagnostics);
        return paths.Select(path => GetReference(path, source, diagnostics)).OfType<MetadataReference>().ToArray();
    }

    private PortableExecutableReference? GetReference(string path, ParsedProject source, List<Diagnostic> diagnostics)
    {
        if (references.TryGetValue(path, out var reference)) return reference;
        try
        {
            reference = MetadataReference.CreateFromFile(path);
            references.Add(path, reference);
            return reference;
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("metadata-invalid", exception.Message, source.Path, "csharp"));
            return null;
        }
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
