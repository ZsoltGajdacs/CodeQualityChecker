using Cqc.Core;

namespace Cqc.Java;

internal sealed record JavaClasspathEntry(string Path, string Kind, string Sha256);
internal sealed record JavaContext(string Id, string ModulePath, string SourceSet, string[] SourceRoots,
    JavaClasspathEntry[] Classpath, string[] ReachableContexts);
internal sealed record JavaContextManifest(string SchemaVersion, string SourceFingerprint, JavaContext[] Contexts);

/// <summary>Stages Maven's ordered module contexts and direct classpath references.</summary>
internal sealed class JavaContextCache(ProjectSnapshot snapshot)
{
    internal const string FileName = "java-contexts.json";
    private readonly JavaClasspathReferences references = new();

    internal Task StageAsync(string runDirectory, MavenMetadata[] modules, CancellationToken token)
    {
        var owners = OutputOwners(modules);
        var contexts = modules.SelectMany(module => module.SourceSets.Select(source => Context(module, source, owners, token)))
            .OrderBy(context => context.Id, StringComparer.Ordinal).ToArray();
        var manifest = new JavaContextManifest("3", snapshot.SourceFingerprint, contexts);
        return EvidenceStore.WriteJsonAsync(new DirectoryInfo(runDirectory), FileName, manifest, token);
    }

    private Dictionary<string, string> OutputOwners(MavenMetadata[] modules)
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            var pom = Relative(module.Pom);
            owners[MavenPaths.Canonical(module.MainOutput)] = pom + "#main";
            owners[MavenPaths.Canonical(module.TestOutput)] = pom + "#test";
            foreach (var artifact in module.Artifacts) owners[MavenPaths.Canonical(artifact.Path)] = pom + "#" + artifact.SourceSet;
        }
        return owners;
    }

    private JavaContext Context(MavenMetadata module, MavenSourceSet source, Dictionary<string, string> owners, CancellationToken token)
    {
        var pom = Relative(module.Pom);
        var id = pom + "#" + source.Name;
        var reachable = source.Classpath.Select(MavenPaths.Canonical).Where(owners.ContainsKey).Select(path => owners[path])
            .Where(owner => owner != id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var roots = source.SourceRoots.Where(Directory.Exists).Select(Relative)
            .Where(root => !root.Split('/').Contains("target", StringComparer.Ordinal))
            .Where(root => snapshot.Files.Any(file => root == "." || file.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal).ToArray();
        var entries = source.Classpath.Where(path => IncludeEntry(path, module)).Select(path => references.Read(path, token)).ToArray();
        return new(id, pom, source.Name, roots, entries, reachable);
    }

    private static bool IncludeEntry(string path, MavenMetadata module)
    {
        if (File.Exists(path) || Directory.Exists(path)) return true;
        // Maven includes empty output directories even for POM-only and source-free modules.
        if (path == module.MainOutput || path == module.TestOutput) return false;
        return true;
    }

    private string Relative(string path) => MavenPaths.ProjectRelative(snapshot.Root, path);
}
