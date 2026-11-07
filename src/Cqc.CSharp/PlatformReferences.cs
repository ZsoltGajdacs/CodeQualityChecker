using System.Text.Json;
using Diagnostic = Cqc.Core.Diagnostic;

namespace Cqc.CSharp;

/// <summary>Only the host framework is implicit; analyzer/application packages are never target references.</summary>
internal static class PlatformReferences
{
    public static string[] Read(List<Diagnostic> diagnostics)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in OwnManifests())
        {
            try { names.UnionWith(RuntimeAssets(manifest)); }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                diagnostics.Add(new("platform-metadata-invalid", "Cannot read framework runtime metadata: " + manifest.Name + ". " + exception.Message, null, "csharp"));
            }
        }
        var platform = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "";
        var paths = platform.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => names.Contains(Path.GetFileName(path))).Order(StringComparer.Ordinal).ToArray();
        if (paths.Length == 0)
            diagnostics.Add(new("platform-metadata-missing", "Framework reference metadata is unavailable. Repair the CQC installation or the host .NET runtime before scoring.", null, "csharp"));
        return paths;
    }

    private static FileInfo[] OwnManifests()
    {
        var directory = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? AppContext.BaseDirectory;
        var framework = new FileInfo(Path.Combine(directory, "Microsoft.NETCore.App.deps.json"));
        return framework.Exists ? [framework] : new DirectoryInfo(AppContext.BaseDirectory).GetFiles("*.deps.json");
    }

    private static IEnumerable<string> RuntimeAssets(FileInfo manifest)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifest.FullName));
        if (!document.RootElement.TryGetProperty("targets", out var targets)) return [];
        return targets.EnumerateObject().SelectMany(target => target.Value.EnumerateObject())
            .Where(library => library.Name.Contains("Microsoft.NETCore.App.Runtime.", StringComparison.OrdinalIgnoreCase))
            .SelectMany(FrameworkFiles).ToArray();
    }

    private static IEnumerable<string> FrameworkFiles(JsonProperty library) => library.Value.TryGetProperty("runtime", out var runtime)
        ? runtime.EnumerateObject().Select(asset => Path.GetFileName(asset.Name)) : [];
}
