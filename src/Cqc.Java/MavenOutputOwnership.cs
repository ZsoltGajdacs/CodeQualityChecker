namespace Cqc.Java;

/// <summary>Maps evaluated output directories and attached artifacts to their source sets.</summary>
internal sealed class MavenOutputOwnership(string root, IEnumerable<MavenMetadata> modules)
{
    internal sealed record Reference(string Path, string[] Contexts);

    private readonly ILookup<string, string> owners = modules.SelectMany(module => Outputs(root, module))
        .ToLookup(output => MavenPaths.Canonical(output.Path), output => output.Context, StringComparer.Ordinal);

    internal Reference[] References(MavenSourceSet sourceSet) => sourceSet.Classpath
        .Select(MavenPaths.Canonical).Distinct(StringComparer.Ordinal)
        .Select(path => new Reference(path, owners[path].Distinct(StringComparer.Ordinal).ToArray())).ToArray();

    private static IEnumerable<(string Path, string Context)> Outputs(string root, MavenMetadata module)
    {
        var pom = MavenPaths.ProjectRelative(root, module.Pom);
        yield return (module.MainOutput, pom + "#main");
        yield return (module.TestOutput, pom + "#test");
        foreach (var artifact in module.Artifacts)
            yield return (artifact.Path, pom + "#" + artifact.SourceSet);
    }
}
