using Cqc.Core;
using Xunit;

namespace Cqc.Tests;

public sealed class DiscoveryRegistrationTests
{
    [Fact]
    public async Task BuildInputsOnlyAffectFingerprintsWhenTheirLanguageIsPresent()
    {
        using var workspace = FixtureWorkspace.Empty();
        var definition = new Cqc.TypeScript.TypeScriptAnalyzer().Definition;
        var request = new AnalysisRequest { ProjectRoot = workspace.Root };
        var initial = await ProjectDiscovery.DiscoverAsync(request, new(), default, [definition]);
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "package.json"), """{"type":"module"}""");
        var unrelated = await ProjectDiscovery.DiscoverAsync(request, new(), default, [definition]);
        Assert.Equal(initial.SourceFingerprint, unrelated.SourceFingerprint);
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "index.ts"), "export const value = 1;");
        var present = await ProjectDiscovery.DiscoverAsync(request, new(), default, [definition]);
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "package.json"), """{"type":"commonjs"}""");
        var changed = await ProjectDiscovery.DiscoverAsync(request, new(), default, [definition]);
        Assert.NotEqual(present.SourceFingerprint, changed.SourceFingerprint);
    }

    [Theory]
    [InlineData(false, "example.parse")]
    [InlineData(true, "example.worker_failed")]
    public async Task RegisteredLanguageRetainsScopedStructuralDiagnostics(bool workerFailed, string code)
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Broken.example"), "broken source");
        var result = await new AnalysisService([new ExampleAnalyzer(workerFailed)]).ReviewAsync(new()
        {
            ProjectRoot = workspace.Root, Scope = "paths", Paths = ["Broken.example"]
        }, CancellationToken.None);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code && diagnostic.Language == "example"
            && diagnostic.Scope == (workerFailed ? "project" : "selected"));
        Assert.Equal(1, Assert.Single(result.Results, family => family.Family == "code").UnscoredFileCount);
    }

    [Fact]
    public async Task AnalysisOnlyRegistrationSkipsEvidencePreparation()
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Broken.example"), "broken source");
        var result = await new AnalysisService([new ExampleAnalyzer(false)]).ReviewAsync(new()
        {
            ProjectRoot = workspace.Root, Execute = true
        }, CancellationToken.None);

        Assert.Contains(result.Modules, module => module.Language == "example");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "example.parse");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "execution_failed");
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".cqc")));
    }

    private sealed class ExampleAnalyzer(bool workerFailed) : ILanguageAnalyzer, ILanguageDiscovery
    {
        public string Language => "example";
        public LanguageDefinition Definition { get; } = new()
        {
            Language = "example", SourceExtensions = [".example"], ManifestPatterns = [],
            InspectManifest = (_, path) => new("example", path, "source-only", false),
            ParticipatesInExecution = false
        };

        public Task<AnalysisFacts> AnalyzeAsync(ProjectSnapshot snapshot, CancellationToken cancellationToken) =>
            Task.FromResult(workerFailed ? new AnalysisFacts([], [], [],
                [new Diagnostic("example.worker_failed", "Worker failed.", Language: Language) { IsAnalyzerFailure = true }], false)
                : new AnalysisFacts([], [], [], [], false)
            {
                SyntaxGaps = [new SyntaxGap("Broken.example", Language, null, "example.parse")]
            });
    }

    [Fact]
    public async Task RegisteringLanguageMetadataAddsDetectionWithoutChangingSharedDiscovery()
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "program.go"), "package sample");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "go.mod"), "module sample");
        var definition = new LanguageDefinition
        {
            Language = "registered-example", SourceExtensions = [".go"], ManifestPatterns = ["go.mod"],
            InspectManifest = (_, path) => new("registered-example", path, "example-build", true)
        };
        var snapshot = await ProjectDiscovery.DiscoverAsync(new() { ProjectRoot = workspace.Root }, new(), CancellationToken.None, [definition]);
        Assert.Equal(["program.go"], snapshot.Files);
        Assert.Equal("registered-example", Assert.Single(snapshot.Modules).Language);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Code == "unsupported_language");
        await File.AppendAllTextAsync(Path.Combine(workspace.Root, "go.mod"), "\ngo 1.0");
        var changed = await ProjectDiscovery.DiscoverAsync(new() { ProjectRoot = workspace.Root }, new(), CancellationToken.None, [definition]);
        Assert.NotEqual(snapshot.SourceFingerprint, changed.SourceFingerprint);
    }

    [Fact]
    public async Task ModuleBuildInputPatternsFingerprintBinaryBuildInputs()
    {
        using var workspace = FixtureWorkspace.Empty();
        Directory.CreateDirectory(Path.Combine(workspace.Root, ".mvn", "wrapper"));
        var wrapper = Path.Combine(workspace.Root, ".mvn", "wrapper", "wrapper.jar");
        await File.WriteAllBytesAsync(wrapper, [0xff, 0]);
        var first = await workspace.Discover();
        await File.WriteAllBytesAsync(wrapper, [0xfe, 0]);
        var second = await workspace.Discover();
        Assert.NotEqual(first.SourceFingerprint, second.SourceFingerprint);
    }

    [Fact]
    public async Task IgnoredMavenWrapperInputsAndExecutableModeAffectFingerprint()
    {
        if (OperatingSystem.IsWindows()) return;
        using var workspace = FixtureWorkspace.Empty();
        var init = await ProcessRunner.RunAsync("git", ["init", "-q"], workspace.Root, CancellationToken.None);
        Assert.Equal(0, init.ExitCode);
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, ".gitignore"), ".mvn/wrapper/\nmvnw\n");
        Directory.CreateDirectory(Path.Combine(workspace.Root, ".mvn", "wrapper"));
        var jar = Path.Combine(workspace.Root, ".mvn", "wrapper", "maven-wrapper.jar");
        var wrapper = Path.Combine(workspace.Root, "mvnw");
        await File.WriteAllTextAsync(jar, "first");
        await File.WriteAllTextAsync(wrapper, "#!/bin/sh\n");
        var first = await workspace.Discover();
        await File.WriteAllTextAsync(jar, "second");
        var second = await workspace.Discover();
        Assert.NotEqual(first.SourceFingerprint, second.SourceFingerprint);
        File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var third = await workspace.Discover();
        Assert.NotEqual(second.SourceFingerprint, third.SourceFingerprint);
    }
}
