using Cqc.Core;
using Xunit;

namespace Cqc.Tests;

public sealed class DiscoveryRegistrationTests
{
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
}
