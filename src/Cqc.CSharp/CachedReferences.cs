using System.Text.Json;
using Cqc.Core;
using Microsoft.CodeAnalysis;
using Diagnostic = Cqc.Core.Diagnostic;

namespace Cqc.CSharp;

internal static class CachedReferences
{
    public static MetadataReference[] Read(ProjectSnapshot snapshot, List<Diagnostic> diagnostics)
    {
        var root = snapshot.Root;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        paths.UnionWith(PlatformReferences.Read(diagnostics));
        foreach (var assets in Directory.EnumerateFiles(root, "project.assets.json", SearchOption.AllDirectories)
                     .Where(x => Path.GetFileName(Path.GetDirectoryName(x)) == "obj"))
        {
            try { AddAssets(assets, paths); }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                diagnostics.Add(new("metadata-unreadable", exception.Message, Path.GetRelativePath(root, assets), "csharp"));
            }
        }
        AddManifest(snapshot, paths, diagnostics);
        var result = new List<MetadataReference>();
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            try { result.Add(MetadataReference.CreateFromFile(path)); }
            catch (Exception exception) when (exception is IOException or BadImageFormatException)
            {
                diagnostics.Add(new("metadata-invalid", exception.Message, null, "csharp"));
            }
        }
        return result.ToArray();
    }

    private static void AddManifest(ProjectSnapshot snapshot, HashSet<string> paths, List<Diagnostic> diagnostics)
    {
        var manifest = Path.Combine(snapshot.Root, ".cqc", "csharp-references.txt");
        if (!File.Exists(manifest)) return;
        var source = manifest + ".source";
        if (!File.Exists(source) || File.ReadAllText(source).Trim() != snapshot.SourceFingerprint)
        {
            diagnostics.Add(new("metadata-stale", "C# reference evidence does not match the source fingerprint. Run explicit execution to refresh it.", null, "csharp"));
            return;
        }
        foreach (var path in File.ReadAllLines(manifest).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (Path.IsPathFullyQualified(path) && File.Exists(path)) paths.Add(path);
            else diagnostics.Add(new("metadata-missing", "A collected C# reference is missing. Run explicit execution to refresh it.", null, "csharp"));
        }
    }

    private static void AddAssets(string path, HashSet<string> references)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (!root.TryGetProperty("targets", out var targets) || !root.TryGetProperty("packageFolders", out var folders)) return;
        var packages = targets.EnumerateObject().SelectMany(x => x.Value.EnumerateObject());
        foreach (var package in packages)
            AddPackage(package, root, folders, references);
    }

    private static void AddPackage(JsonProperty package, JsonElement root, JsonElement folders, HashSet<string> references)
    {
        if (!package.Value.TryGetProperty("compile", out var compile)) return;
        var packagePath = PackagePath(package.Name, root);
        var entries = compile.EnumerateObject().Where(x => x.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        foreach (var entry in entries)
        {
            foreach (var folder in folders.EnumerateObject())
            {
                var candidate = Path.Combine(folder.Name, packagePath, entry.Name);
                if (File.Exists(candidate)) references.Add(Path.GetFullPath(candidate));
            }
        }
    }

    private static string PackagePath(string name, JsonElement root)
    {
        if (!root.TryGetProperty("libraries", out var libraries)) return name.ToLowerInvariant();
        if (!libraries.TryGetProperty(name, out var library)) return name.ToLowerInvariant();
        return library.TryGetProperty("path", out var path) ? path.GetString() ?? name.ToLowerInvariant() : name.ToLowerInvariant();
    }
}
