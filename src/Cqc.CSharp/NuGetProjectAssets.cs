using System.Text.Json;
using Diagnostic = Cqc.Core.Diagnostic;

namespace Cqc.CSharp;

/// <summary>Reads compile-time package references for one project and one target framework.</summary>
internal sealed class NuGetProjectAssets(string root, ParsedProject source)
{
    private readonly string relative = Path.Combine(Path.GetDirectoryName(source.Path) ?? "", "obj", "project.assets.json");

    public string[] Read(List<Diagnostic> diagnostics)
    {
        var path = Path.Combine(root, relative);
        if (!File.Exists(path)) return [];
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return Paths(document.RootElement);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("metadata-unreadable", exception.Message, relative, "csharp"));
            return [];
        }
    }

    private string[] Paths(JsonElement assets)
    {
        if (!assets.TryGetProperty("targets", out var targets) || !assets.TryGetProperty("packageFolders", out var folders)) return [];
        var packageFolders = folders.EnumerateObject().Select(folder => folder.Name).ToArray();
        assets.TryGetProperty("libraries", out var libraries);
        return Target(targets).EnumerateObject().Where(IsPackage).SelectMany(package => CompilePaths(package, libraries, packageFolders))
            .Where(File.Exists).Select(Path.GetFullPath).ToArray();
    }

    private JsonElement Target(JsonElement targets)
    {
        var framework = source.Options.TargetFramework;
        var candidates = targets.EnumerateObject().Where(target => MatchesTarget(target, framework)).ToArray();
        if (candidates.Length != 1) throw new JsonException("No unique NuGet assets target matches this C# project.");
        return candidates[0].Value;
    }

    private static bool MatchesTarget(JsonProperty target, string? framework) =>
        !target.Name.Contains('/') && (framework is null || target.Name.Equals(framework, StringComparison.OrdinalIgnoreCase));

    private static bool IsPackage(JsonProperty package) =>
        !package.Value.TryGetProperty("type", out var type) || type.GetString() != "project";

    private static IEnumerable<string> CompilePaths(JsonProperty package, JsonElement libraries, string[] folders)
    {
        if (!package.Value.TryGetProperty("compile", out var compile)) return [];
        var packagePath = PackagePath(package, libraries);
        var assemblies = compile.EnumerateObject().Where(entry => entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        return assemblies.SelectMany(entry => folders.Select(folder => Path.Combine(folder, packagePath, entry.Name)));
    }

    private static string PackagePath(JsonProperty package, JsonElement libraries) =>
        ReadPackagePath(package, libraries) ?? package.Name.ToLowerInvariant();

    private static string? ReadPackagePath(JsonProperty package, JsonElement libraries)
    {
        if (libraries.ValueKind == JsonValueKind.Undefined || !libraries.TryGetProperty(package.Name, out var library)) return null;
        return library.TryGetProperty("path", out var path) ? path.GetString() : null;
    }
}
