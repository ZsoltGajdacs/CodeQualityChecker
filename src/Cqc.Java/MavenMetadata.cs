using System.Xml.Linq;

namespace Cqc.Java;

/// <summary>Evaluated Maven output, captured inside the isolated build by the bundled extension.</summary>
internal sealed record MavenMetadata
{
    public required string Pom { get; init; }
    public required string BuildDirectory { get; init; }
    public required string MainOutput { get; init; }
    public required string TestOutput { get; init; }
    public required MavenSourceSet[] SourceSets { get; init; }
    public MavenArtifact[] Artifacts { get; init; } = [];
    public MavenCoverage? Coverage { get; init; }

    internal static MavenMetadata Read(string file)
    {
        var project = XDocument.Load(file).Root ?? throw new InvalidOperationException("Empty Maven metadata.");
        var coverage = project.Element("coverage");
        return new() { Pom = Required(project, "pom"), BuildDirectory = Required(project, "buildDirectory"),
            MainOutput = Required(project, "mainOutput"), TestOutput = Required(project, "testOutput"),
            Coverage = coverage is null ? null : new(
                (string?)coverage.Attribute("version"), (string?)coverage.Attribute("report"), (string?)coverage.Attribute("reason")),
            Artifacts = project.Elements("artifact").Select(artifact => new MavenArtifact(artifact.Value, artifact.Attribute("sourceSet")?.Value
                ?? throw new InvalidOperationException("Missing artifact source set."))).ToArray(),
            SourceSets = project.Elements("context").Select(context => new MavenSourceSet(
                context.Attribute("sourceSet")?.Value ?? throw new InvalidOperationException("Missing Maven source set."),
                context.Elements("sourceRoot").Select(element => element.Value).ToArray(),
                context.Elements("classpath").Select(element => element.Value).ToArray())).ToArray() };
    }

    private static string Required(XElement element, string name) => element.Element(name)?.Value
        ?? throw new InvalidOperationException("Missing Maven metadata field: " + name);
}

internal sealed record MavenArtifact(string Path, string SourceSet);
internal sealed record MavenSourceSet(string Name, string[] SourceRoots, string[] Classpath);
internal sealed record MavenCoverage(string? Version, string? Report, string? Reason);

internal static class MavenCollector
{
    internal static string Find()
    {
        var packaged = Path.Combine(AppContext.BaseDirectory, "maven-metadata.jar");
        if (File.Exists(packaged)) return packaged;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "maven-metadata", "target", "maven-metadata.jar");
            if (File.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException("Maven metadata collector is missing. Reinstall the matching CQC distribution or build maven-metadata/pom.xml.");
    }
}
