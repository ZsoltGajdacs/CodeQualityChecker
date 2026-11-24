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
    public async Task MavenWrapperIsPreferredAndFailureDoesNotFallBack()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        var snapshot = WrapperProject(fixture, "#!/bin/sh\nif [ \"$1\" = \"--version\" ]; then echo wrapper-version; exit 0; fi\necho wrapper-failure >&2\nexit 19\n");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new BuildExecutor([new JavaAnalyzer()]).CollectAsync(snapshot, CancellationToken.None));
        Assert.Contains("wrapper-failure", error.Message);
        Assert.Contains("19", error.Message);
    }

    [Fact]
    public async Task MavenWrapperCanReadExistingProjectOutputsAndStagesOnlyCurrentRunEvidence()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        fixture.Write("target/existing-output", "from a prior build");
        fixture.Write("target/cqc-metadata.xml", "stale metadata must not be read");
        var snapshot = WrapperProject(fixture, """
            #!/bin/sh
            set -eu
            if [ "$1" = "--version" ]; then echo wrapper-version; exit 0; fi
            cp target/existing-output target/wrapper-ran
            """ + "\n" + MetadataScript());
        var build = await new BuildExecutor([new JavaAnalyzer()]).CollectAsync(snapshot, CancellationToken.None);
        using (build)
        {
            Assert.Equal("wrapper-version", Assert.Single(build.Results).ToolVersions["maven:pom.xml"]);
            Assert.Empty(Assert.Single(build.Results).Reports);
            Assert.True(File.Exists(Path.Combine(build.RunDirectory, "java-contexts.json")));
            Assert.False(File.Exists(Path.Combine(fixture.Root, ".cqc/java-contexts.json")));
        }
        Assert.False(Directory.Exists(build.RunDirectory));
        Assert.Equal("from a prior build", File.ReadAllText(Path.Combine(fixture.Root, "target/wrapper-ran")));
    }

    [Fact]
    public async Task MissingMavenEvidenceIsReportedInsteadOfAccepted()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        var snapshot = WrapperProject(fixture, "#!/bin/sh\necho ok\n");
        using var build = await new BuildExecutor([new JavaAnalyzer()]).CollectAsync(snapshot, CancellationToken.None);
        Assert.Contains(Assert.Single(build.Results).Diagnostics, d => d.Code == "java.classpath_missing");
    }

    [Fact]
    public async Task PomOnlyModulesDoNotReportMissingCoverage()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        var snapshot = WrapperProject(fixture, "#!/bin/sh\n" + MetadataScript());
        using var build = await new BuildExecutor([new JavaAnalyzer()]).CollectAsync(snapshot, CancellationToken.None);
        Assert.DoesNotContain(Assert.Single(build.Results).Diagnostics, d => d.Code is "java.coverage_missing" or "java.classpath_missing");
    }

    [Fact]
    public async Task MissingEvidenceForOneRootKeepsOtherRootReportsAndInvalidatesClasspath()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        fixture.Write("one/pom.xml", "<project />");
        fixture.Write("one/mvnw", "#!/bin/sh\necho ok\n");
        fixture.Write("two/pom.xml", "<project />");
        fixture.Write("two/mvnw", "#!/bin/sh\n" + MetadataScript(coverage: true));
        foreach (var path in new[] { "one/mvnw", "two/mvnw" }) File.SetUnixFileMode(Path.Combine(fixture.Root, path), UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var snapshot = new ProjectSnapshot(fixture.Root, [], [], [Maven("one/pom.xml"), Maven("two/pom.xml")], "fingerprint", []);
        using var build = await new BuildExecutor([new JavaAnalyzer()]).CollectAsync(snapshot, CancellationToken.None);
        var result = Assert.Single(build.Results);
        Assert.Single(result.Reports);
        Assert.Equal("two/pom.xml", Assert.Single(result.ReportModules).Value);
        Assert.Contains(result.Diagnostics, d => d.Code == "java.classpath_missing" && d.File == "one/pom.xml");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "java.coverage_missing");
        Assert.True(File.Exists(Path.Combine(build.RunDirectory, "java-contexts.json")));
    }

    [Fact]
    public async Task MissingClasspathJarInvalidatesCachedClasspath()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        var snapshot = WrapperProject(fixture, "#!/bin/sh\n" + MetadataScript("/missing-cqc-fixture/dependency.jar"));
        using var build = await new BuildExecutor([new JavaAnalyzer()]).CollectAsync(snapshot, CancellationToken.None);
        Assert.Contains(Assert.Single(build.Results).Diagnostics, d => d.Code == "java.classpath_missing" && d.Message.Contains("dependency.jar"));
        Assert.Contains("0000000000000000000000000000000000000000000000000000000000000000",
            File.ReadAllText(Path.Combine(build.RunDirectory, "java-contexts.json")));
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
        fixture.Write("pom.xml", "<project />");
        File.CreateSymbolicLink(Path.Combine(fixture.Root, "mvnw"), "missing-wrapper");
        var snapshot = new ProjectSnapshot(fixture.Root, [], [], [Maven("pom.xml")], "fingerprint", []);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new BuildExecutor([new JavaAnalyzer()]).CollectAsync(snapshot, CancellationToken.None));
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
    public async Task BuildThatChangesInputsDoesNotReplacePublishedEvidence()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        WrapperProject(fixture, "#!/bin/sh\n" + MetadataScript(beforeCapture: "printf '\\n' >> pom.xml\n"));
        fixture.Write(".cqc/java-contexts.json", "previous contexts");
        fixture.Write(".cqc/coverage.json", "previous coverage");
        var service = new AnalysisService([new JavaAnalyzer()]);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnalyzeAsync(
            new() { ProjectRoot = fixture.Root, Execute = true }, ["code"], CancellationToken.None));
        Assert.Contains("Source/build inputs changed during execution", error.Message);
        Assert.Equal("previous contexts", File.ReadAllText(Path.Combine(fixture.Root, ".cqc/java-contexts.json")));
        Assert.Equal("previous coverage", File.ReadAllText(Path.Combine(fixture.Root, ".cqc/coverage.json")));
    }

    [Fact]
    public async Task ManifestPublicationReplacesASymlinkWithoutChangingItsTarget()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        WrapperProject(fixture, "#!/bin/sh\n" + MetadataScript());
        fixture.Write(".cqc/placeholder", "");
        fixture.Write("Tracked.java", "class Tracked {}");
        var source = Path.Combine(fixture.Root, "Tracked.java");
        var manifest = Path.Combine(fixture.Root, ".cqc/java-contexts.json");
        File.CreateSymbolicLink(manifest, source);

        var service = new AnalysisService([new JavaAnalyzer()]);
        var result = await service.AnalyzeAsync(new() { ProjectRoot = fixture.Root, Execute = true }, ["code"], CancellationToken.None);
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "execution_failed");
        Assert.Null(new FileInfo(manifest).LinkTarget);
        Assert.Equal("class Tracked {}", File.ReadAllText(source));
    }

    [Fact]
    public async Task PublicationWithAnOccupiedCoveragePathLeavesPreviousManifestInPlace()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        WrapperProject(fixture, "#!/bin/sh\n" + MetadataScript());
        fixture.Write(".cqc/java-contexts.json", "previous contexts");
        var coverage = Directory.CreateDirectory(Path.Combine(fixture.Root, ".cqc/coverage.json")).FullName;
        var service = new AnalysisService([new JavaAnalyzer()]);

        await Assert.ThrowsAnyAsync<IOException>(() => service.AnalyzeAsync(
            new() { ProjectRoot = fixture.Root, Execute = true }, ["code"], CancellationToken.None));
        Assert.Equal("previous contexts", File.ReadAllText(Path.Combine(fixture.Root, ".cqc/java-contexts.json")));
        Assert.True(Directory.Exists(coverage));
    }

    [Fact]
    public async Task CheckoutAliasesSerializeInPlaceBuilds()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ScratchProject();
        using var parent = new ScratchProject();
        var alias = Path.Combine(parent.Root, "alias");
        Directory.CreateSymbolicLink(alias, fixture.Root);
        WrapperProject(fixture, "#!/bin/sh\n" + MetadataScript(beforeCapture: """
            mkdir target/build-in-progress
            trap 'rmdir target/build-in-progress' EXIT
            while [ ! -f target/allow-build-to-finish ]; do sleep 0.05; done
            """ + "\n"));
        var service = new AnalysisService([new JavaAnalyzer()]);
        var request = new AnalysisRequest { ProjectRoot = fixture.Root, Execute = true, TimeoutSeconds = 20 };
        var first = service.AnalyzeAsync(request, ["code"], CancellationToken.None);
        var marker = Path.Combine(fixture.Root, "target", "build-in-progress");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!Directory.Exists(marker)) await Task.Delay(25, timeout.Token);

        var second = service.AnalyzeAsync(request with { ProjectRoot = alias }, ["code"], CancellationToken.None);
        await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(1)));
        fixture.Write("target/allow-build-to-finish", "");
        var results = await Task.WhenAll(first, second);
        Assert.All(results, result => Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "execution_failed"));
        Assert.False(Directory.Exists(marker));
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

    private static string MetadataScript(string? missingJar = null, bool coverage = false, string beforeCapture = "") =>
        """
        set -eu
        if [ "$1" = "--version" ]; then echo wrapper-version; exit 0; fi
        for argument in "$@"; do
          case "$argument" in -Dcqc.reactorDirectory=*) reactor="${argument#*=}" ;; esac
        done
        output="$reactor/module-fixture"
        mkdir -p "$output" target
        """ + "\n" + beforeCapture + (coverage ? "touch \"$output/jacoco.xml\"\n" : "") +
        "cat > \"$output/metadata.xml\" <<EOF\n" +
        "<project><pom>$PWD/pom.xml</pom>" +
        "<mainOutput>$PWD/target/classes</mainOutput><testOutput>$PWD/target/test-classes</testOutput>" +
        (coverage ? "<coverage version='0.8.15' report='$output/jacoco.xml'/>" : "") +
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
