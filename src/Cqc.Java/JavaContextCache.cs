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
        var owners = new MavenOutputOwnership(snapshot.Root, modules);
        var contexts = modules.SelectMany(module => module.SourceSets.Select(source => Context(module, source, owners, token)))
            .OrderBy(context => context.Id, StringComparer.Ordinal).ToArray();
        var manifest = new JavaContextManifest("3", snapshot.SourceFingerprint, contexts);
        return EvidenceStore.WriteJsonAsync(new DirectoryInfo(runDirectory), FileName, manifest, token);
    }

    private JavaContext Context(MavenMetadata module, MavenSourceSet source, MavenOutputOwnership owners, CancellationToken token)
    {
        var pom = Relative(module.Pom);
        var id = pom + "#" + source.Name;
        var reachable = owners.References(source).SelectMany(reference => reference.Contexts)
            .Where(owner => owner != id && (source.Name == "test" || owner != pom + "#test"))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
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
