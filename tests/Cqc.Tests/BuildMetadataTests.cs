using Cqc.Core;
using Cqc.Java;
using Xunit;

namespace Cqc.Tests;

public sealed class BuildMetadataTests
{
    [Fact]
    public void NestedIndependentMavenProjectsAreNotMistakenForAggregatedModules()
    {
        using var fixture = new ScratchProject();
        fixture.Write("pom.xml", "<project><modules><module>declared</module></modules></project>");
        fixture.Write("declared/pom.xml", "<project />");
        fixture.Write("independent/pom.xml", "<project />");
        var modules = new[] { Maven("pom.xml"), Maven("declared/pom.xml"), Maven("independent/pom.xml") };
        Assert.Equal(new[] { "pom.xml", "independent/pom.xml" }, Cqc.Java.MavenBuildRoots.Select(modules, fixture.Root).Select(m => m.Path));
    }

    [Fact]
    public async Task NewBuildMetadataReplacesRemovedDependenciesInsteadOfCertifyingThemAsFresh()
    {
        using var fixture = new ScratchProject();
        fixture.Write(".cqc/java-classpath.txt", "/old/dependency.jar\n");
        fixture.Write(".cqc/java-classpath.txt.source", "old-source");
        fixture.Write(".cqc/csharp-references.txt", "/old/assembly.dll\n");
        var snapshot = new ProjectSnapshot(fixture.Root, [], [], [], "current-source", []);
        var cache = new ArtifactCache(snapshot);
        await cache.ReplaceLinesAsync("java-classpath.txt", ["/new/dependency.jar"], CancellationToken.None);
        await cache.ReplaceLinesAsync("csharp-references.txt", [], CancellationToken.None);
        Assert.Equal(new[] { "/new/dependency.jar" }, File.ReadAllLines(Path.Combine(fixture.Root, ".cqc/java-classpath.txt")));
        Assert.All(File.ReadAllLines(Path.Combine(fixture.Root, ".cqc/csharp-references.txt")), line => Assert.True(string.IsNullOrEmpty(line)));
        Assert.Equal("current-source", File.ReadAllText(Path.Combine(fixture.Root, ".cqc/java-classpath.txt.source")));
    }

    [Fact]
    public void CopySkipsBuildOutputsCachesAndSymbolicLinksButPreservesBuildConfiguration()
    {
        using var fixture = new ScratchProject();
        using var destination = new ScratchProject();
        fixture.Write(".mvn/maven.config", "-s settings.xml");
        fixture.Write("settings.xml", "<settings />");
        fixture.Write("target/generated.java", "generated");
        fixture.Write(".cqc/coverage.json", "cached");
        fixture.Write("src/Real.java", "class Real {}");
        File.CreateSymbolicLink(Path.Combine(fixture.Root, "src/Linked.java"), Path.Combine(fixture.Root, "src/Real.java"));
        SnapshotCopy.Copy(fixture.Root, destination.Root);
        Assert.True(File.Exists(Path.Combine(destination.Root, ".mvn/maven.config")));
        Assert.True(File.Exists(Path.Combine(destination.Root, "settings.xml")));
        Assert.True(File.Exists(Path.Combine(destination.Root, "src/Real.java")));
        Assert.False(File.Exists(Path.Combine(destination.Root, "src/Linked.java")));
        Assert.False(Directory.Exists(Path.Combine(destination.Root, "target")));
        Assert.False(Directory.Exists(Path.Combine(destination.Root, ".cqc")));
    }

    [Fact]
    public async Task ProvenanceFileLinksAreReplacedWithoutChangingTheLinkedSource()
    {
        using var fixture = new ScratchProject();
        fixture.Write("Source.cs", "class Source {}");
        fixture.Write(".cqc/placeholder", "");
        File.CreateSymbolicLink(Path.Combine(fixture.Root, ".cqc/java-classpath.txt.source"), Path.Combine(fixture.Root, "Source.cs"));
        var snapshot = new ProjectSnapshot(fixture.Root, [], [], [], "current-source", []);
        await new ArtifactCache(snapshot).ReplaceLinesAsync("java-classpath.txt", [], CancellationToken.None);
        Assert.Equal("class Source {}", File.ReadAllText(Path.Combine(fixture.Root, "Source.cs")));
        Assert.Equal("current-source", File.ReadAllText(Path.Combine(fixture.Root, ".cqc/java-classpath.txt.source")));
    }

    [Fact]
    public void JavaCoverageDoesNotSelectAnArbitraryBestResultFromOverlappingReports()
    {
        using var fixture = new ScratchProject();
        fixture.Write("jacoco.xml", "<report><package name=''><class name='Example'><method name='read' desc='()I'><counter type='LINE' covered='1' missed='0'/></method></class></package></report>");
        var report = Path.Combine(fixture.Root, "jacoco.xml");
        var method = new MethodFact("java:Example.read()", "java:Example", "java", new("Example.java", 1, 1), 1, 1,
            false, false, false, 0, [], [], 1);
        var execution = new JavaExecution();
        var single = new LanguageBuildResult("java", [report], new(StringComparer.Ordinal));
        Assert.Equal(method.Id, Assert.Single(execution.ReadCoverage(single, [method])).MethodId);
        Assert.Empty(execution.ReadCoverage(single with { Reports = [report, report] }, [method]));
    }

    private static ProjectModule Maven(string path) => new("java", path, "maven", true);

    private sealed class ScratchProject : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "cqc-build-test-" + Guid.NewGuid().ToString("N"));
        public ScratchProject() => Directory.CreateDirectory(Root);
        public void Write(string path, string text)
        {
            var full = Path.Combine(Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
