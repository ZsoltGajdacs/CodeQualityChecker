using System.Text.Json;
using System.IO.Compression;
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
    public void MavenBuildRootsAcceptModulePomPathsAndReportMalformedManifests()
    {
        using var fixture = new ScratchProject();
        fixture.Write("pom.xml", "<project><modules><module>child/pom.xml</module></modules></project>");
        fixture.Write("child/pom.xml", "<project />");
        var modules = new[] { Maven("pom.xml"), Maven("child/pom.xml") };
        Assert.Equal("pom.xml", Assert.Single(Cqc.Java.MavenBuildRoots.Select(modules, fixture.Root)).Path);
        fixture.Write("child/pom.xml", "<project>");
        var error = Assert.Throws<InvalidOperationException>(() => Cqc.Java.MavenBuildRoots.Select(modules, fixture.Root).ToArray());
        Assert.Contains("child/pom.xml", error.Message);
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
    public async Task MavenContextCacheRetainsJarsAndCompiledDirectoryContents()
    {
        using var fixture = new ScratchProject();
        using var scratch = new ScratchProject();
        scratch.Write("module/target/dependency.jar", "jar content");
        scratch.Write("module/target/classes/Generated.class", "compiled content");
        var classes = Path.Combine(scratch.Root, "module/target/classes");
        var jar = Path.Combine(scratch.Root, "module/target/dependency.jar");
        var module = new MavenMetadata { Pom = Path.Combine(scratch.Root, "module/pom.xml"),
            BuildDirectory = Path.Combine(scratch.Root, "module/target"), MainOutput = classes,
            TestOutput = Path.Combine(scratch.Root, "module/target/test-classes"),
            SourceSets = [new("main", [], [classes, jar])] };
        await new JavaContextCache(new(fixture.Root, [], [], [], "source", []), scratch.Root)
            .PublishAsync([module], CancellationToken.None);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, ".cqc/java-contexts.json")));
        var retained = manifest.RootElement.GetProperty("contexts")[0].GetProperty("classpath").EnumerateArray().ToArray();
        var compiled = retained[0].GetProperty("path").GetString()!;
        using var archive = ZipFile.OpenRead(compiled);
        Assert.NotNull(archive.GetEntry("Generated.class"));
        Assert.Equal("jar content", await File.ReadAllTextAsync(retained[1].GetProperty("path").GetString()!));
        Assert.All(retained, entry => Assert.StartsWith(Path.Combine(fixture.Root, ".cqc/references"), entry.GetProperty("path").GetString()));
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
    public void SnapshotPreservesWrapperExecutableMode()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        using var destination = new ScratchProject();
        fixture.Write("mvnw", "#!/bin/sh\nexit 0\n");
        var wrapper = Path.Combine(fixture.Root, "mvnw");
        File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        SnapshotCopy.Copy(fixture.Root, destination.Root);
        Assert.Equal(File.GetUnixFileMode(wrapper), File.GetUnixFileMode(Path.Combine(destination.Root, "mvnw")));
    }

    [Fact]
    public async Task MavenWrapperIsPreferredAndFailureDoesNotFallBack()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        var snapshot = WrapperProject(fixture, "#!/bin/sh\nif [ \"$1\" = \"--version\" ]; then echo wrapper-version; exit 0; fi\necho wrapper-failure >&2\nexit 19\n");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new JavaExecution().ExecuteAsync(snapshot, fixture.Root, CancellationToken.None));
        Assert.Contains("wrapper-failure", error.Message);
        Assert.Contains("19", error.Message);
    }

    [Fact]
    public async Task MavenWrapperRunsInCopyAndRecordsItsVersion()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        using var scratch = new ScratchProject();
        fixture.Write("pom.xml", "<project />");
        fixture.Write("mvnw", "#!/bin/sh\nif [ \"$1\" = \"--version\" ]; then echo wrapper-version; else echo used > wrapper-ran; mkdir -p target/site/jacoco; touch target/site/jacoco/jacoco.xml; " + MetadataScript() + "fi\n");
        File.SetUnixFileMode(Path.Combine(fixture.Root, "mvnw"), UnixFileMode.UserRead | UnixFileMode.UserExecute);
        SnapshotCopy.Copy(fixture.Root, scratch.Root);
        var snapshot = new ProjectSnapshot(fixture.Root, [], [], [Maven("pom.xml")], "fingerprint", []);
        var build = await new JavaExecution().ExecuteAsync(snapshot, scratch.Root, CancellationToken.None);
        Assert.Equal("wrapper-version", build.ToolVersions["maven:pom.xml"]);
        Assert.Empty(build.Reports);
        Assert.True(File.Exists(Path.Combine(scratch.Root, "wrapper-ran")));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "wrapper-ran")));
    }

    [Fact]
    public async Task MissingMavenEvidenceIsReportedInsteadOfAccepted()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        var snapshot = WrapperProject(fixture, "#!/bin/sh\necho ok\n");
        var build = await new JavaExecution().ExecuteAsync(snapshot, fixture.Root, CancellationToken.None);
        Assert.Contains(build.Diagnostics, d => d.Code == "java.classpath_missing");
    }

    [Fact]
    public async Task PomOnlyModulesDoNotReportMissingCoverage()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        var snapshot = WrapperProject(fixture, "#!/bin/sh\n" + MetadataScript());
        var build = await new JavaExecution().ExecuteAsync(snapshot, fixture.Root, CancellationToken.None);
        Assert.DoesNotContain(build.Diagnostics, d => d.Code is "java.coverage_missing" or "java.classpath_missing");
    }

    [Fact]
    public async Task MissingEvidenceForOneRootKeepsOtherRootReportsAndInvalidatesClasspath()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        fixture.Write("one/pom.xml", "<project />");
        fixture.Write("one/mvnw", "#!/bin/sh\necho ok\n");
        fixture.Write("two/pom.xml", "<project />");
        fixture.Write("two/mvnw", "#!/bin/sh\nmkdir -p target/cqc-coverage\ntouch target/cqc-coverage/jacoco.xml\n" + MetadataScript(coverage: true));
        foreach (var path in new[] { "one/mvnw", "two/mvnw" }) File.SetUnixFileMode(Path.Combine(fixture.Root, path), UnixFileMode.UserRead | UnixFileMode.UserExecute);
        fixture.Write(".cqc/java-classpath.txt.source", "fingerprint");
        var snapshot = new ProjectSnapshot(fixture.Root, [], [], [Maven("one/pom.xml"), Maven("two/pom.xml")], "fingerprint", []);
        var build = await new JavaExecution().ExecuteAsync(snapshot, fixture.Root, CancellationToken.None);
        Assert.Single(build.Reports);
        Assert.Equal("two/pom.xml", Assert.Single(build.ReportModules).Value);
        Assert.Contains(build.Diagnostics, d => d.Code == "java.classpath_missing" && d.File == "one/pom.xml");
        Assert.DoesNotContain(build.Diagnostics, d => d.Code == "java.coverage_missing");
        Assert.True(File.Exists(Path.Combine(fixture.Root, ".cqc/java-contexts.json")));
    }

    [Fact]
    public async Task MissingClasspathJarInvalidatesCachedClasspath()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        var snapshot = WrapperProject(fixture, "#!/bin/sh\n" + MetadataScript("/missing-cqc-fixture/dependency.jar"));
        fixture.Write(".cqc/java-classpath.txt.source", "fingerprint");
        var build = await new JavaExecution().ExecuteAsync(snapshot, fixture.Root, CancellationToken.None);
        Assert.Contains(build.Diagnostics, d => d.Code == "java.classpath_missing" && d.Message.Contains("dependency.jar"));
        Assert.Contains("0000000000000000000000000000000000000000000000000000000000000000", File.ReadAllText(Path.Combine(fixture.Root, ".cqc/java-contexts.json")));
    }

    [Fact]
    public async Task FirstIncompleteCollectionReportsStaleClasspathOnReplay()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        WrapperProject(fixture, "#!/bin/sh\necho ok\n");
        fixture.Write("src/main/java/example/Sample.java", "package example; public class Sample { public int value() { return 1; } }");
        var service = new AnalysisService([new JavaAnalyzer()]);
        var request = new AnalysisRequest { ProjectRoot = fixture.Root, Execute = true, TimeoutSeconds = 60 };
        var first = await service.AnalyzeAsync(request, ["dependencies"], CancellationToken.None);
        Assert.Contains(first.Diagnostics, d => d.Code == "java.classpath_missing");
        Assert.Contains(first.Diagnostics, d => d.Code == "java.context_unverified");
        var replay = await service.AnalyzeAsync(request with { Execute = false }, ["dependencies"], CancellationToken.None);
        Assert.Contains(replay.Diagnostics, d => d.Code == "java.context_unverified");
    }

    [Fact]
    public void CyclicMavenAggregationReportsNoBuildRoot()
    {
        using var fixture = new ScratchProject();
        fixture.Write("a/pom.xml", "<project><modules><module>../b</module></modules></project>");
        fixture.Write("b/pom.xml", "<project><modules><module>../a</module></modules></project>");
        var modules = new[] { Maven("a/pom.xml"), Maven("b/pom.xml") };
        var error = Assert.Throws<InvalidOperationException>(() => MavenBuildRoots.Select(modules, fixture.Root).ToArray());
        Assert.Contains("cyclic", error.Message);
    }

    [Fact]
    public void MavenToolSearchIsBoundedByProjectRootAndPrefersNearestWrapper()
    {
        using var fixture = new ScratchProject();
        fixture.Write("mvnw", "root");
        fixture.Write("project/pom.xml", "<project />");
        var projectRoot = Path.Combine(fixture.Root, "project");
        var pom = Path.Combine(projectRoot, "pom.xml");
        Assert.Equal("mvn", MavenTool.Select(projectRoot, pom));
        fixture.Write("project/mvnw", "project");
        Assert.Equal(Path.Combine(projectRoot, "mvnw"), MavenTool.Select(projectRoot, pom));
    }

    [Fact]
    public async Task DanglingWrapperLinkIsRejectedWithoutFallingBack()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        using var scratch = new ScratchProject();
        fixture.Write("pom.xml", "<project />");
        File.CreateSymbolicLink(Path.Combine(fixture.Root, "mvnw"), "missing-wrapper");
        SnapshotCopy.Copy(fixture.Root, scratch.Root);
        var snapshot = new ProjectSnapshot(fixture.Root, [], [], [Maven("pom.xml")], "fingerprint", []);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new JavaExecution().ExecuteAsync(snapshot, scratch.Root, CancellationToken.None));
        Assert.Contains("Maven Wrapper", error.Message);
    }

    [Fact]
    public void WrapperInspectionDistinguishesMissingAndUnexecutableTools()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        fixture.Write("pom.xml", "<project />");
        var snapshot = new ProjectSnapshot(fixture.Root, [], [], [Maven("pom.xml")], "fingerprint", []);
        var inspect = new JavaAnalyzer().Definition.InspectPrerequisites!;
        Assert.Contains(inspect(snapshot, _ => false), d => d.Code == "missing_build_tool");
        fixture.Write("mvnw", "#!/bin/sh\n");
        File.SetUnixFileMode(Path.Combine(fixture.Root, "mvnw"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Contains(inspect(snapshot, _ => true), d => d.Code == "maven_wrapper_unexecutable");
        File.SetUnixFileMode(Path.Combine(fixture.Root, "mvnw"), UnixFileMode.UserRead | UnixFileMode.UserExecute);
        Assert.Empty(inspect(snapshot, _ => false));
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

    private static string MetadataScript(string? missingJar = null, bool coverage = false) =>
        "mkdir -p target\ncat > target/cqc-metadata.xml <<EOF\n" +
        "<project><pom>$PWD/pom.xml</pom><buildDirectory>$PWD/target</buildDirectory>" +
        "<mainOutput>$PWD/target/classes</mainOutput><testOutput>$PWD/target/test-classes</testOutput>" +
        (coverage ? "<coverage version='0.8.15' report='$PWD/target/cqc-coverage/jacoco.xml'/>" : "") +
        "<context sourceSet='main'>" + (missingJar is null ? "" : "<classpath>" + missingJar + "</classpath>") +
        "</context><context sourceSet='test'/></project>\nEOF\n";

    private static ProjectModule Maven(string path) => new("java", path, "maven", true);

    private static ProjectSnapshot WrapperProject(ScratchProject fixture, string script)
    {
        fixture.Write("pom.xml", "<project />");
        fixture.Write("mvnw", script);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(fixture.Root, "mvnw"), UnixFileMode.UserRead | UnixFileMode.UserExecute);
        return new(fixture.Root, [], [], [Maven("pom.xml")], "fingerprint", []);
    }

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
