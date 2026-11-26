using System.Text;
using Cqc.Core;
using Xunit;

namespace Cqc.Tests;

public sealed class HotspotConfigurationTests
{
    private static string ProjectConfigPath(FixtureWorkspace workspace) =>
        Path.Combine(workspace.Root, ".cqc", "config.json");

    [Fact]
    public async Task ProjectBoundsCombineWithGeneralSettingsAndExplicitConfigTakesPrecedence()
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, ".cqc.json"), """{"complexityThreshold": 12}""", Encoding.UTF8);
        var projectFile = ProjectConfigPath(workspace);
        Directory.CreateDirectory(Path.GetDirectoryName(projectFile)!);
        await File.WriteAllTextAsync(projectFile, """{"hotspots":{"constructorParameters":{"upper":14}}}""");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "override.json"),
            """{"hotspots":{"constructorParameters":{"lower":5}}}""", Encoding.Unicode);

        var (project, projectFingerprint) = Configuration.Load(workspace.Root, null);
        Assert.Equal(12, project.ComplexityThreshold);
        Assert.Equal(new HotspotRange(7, 14), project.Hotspots.ConstructorParameters);

        var (specified, specifiedFingerprint) = Configuration.Load(workspace.Root, "override.json");
        Assert.Equal(8, specified.ComplexityThreshold);
        Assert.Equal(new HotspotRange(5, 10), specified.Hotspots.ConstructorParameters);
        Assert.NotEqual(projectFingerprint, specifiedFingerprint);
    }

    [Theory]
    [InlineData("""{"hotspots":{"classLines":{"lower":-1}}}""", "classLines.lower")]
    [InlineData("""{"hotspots":{"editCommits":{"upper":10}}}""", "editCommits.upper")]
    [InlineData("""{"hotspots":{"constructorParameters":{"upper":2.5}}}""", "constructorParameters.upper")]
    [InlineData("""{"hotspots":{"editCommits":null}}""", "editCommits")]
    [InlineData("""{"hotspots":null}""", "hotspots")]
    public async Task InvalidBoundsIdentifyTheFileAndSetting(string json, string setting)
    {
        using var workspace = FixtureWorkspace.Empty();
        var projectFile = ProjectConfigPath(workspace);
        Directory.CreateDirectory(Path.GetDirectoryName(projectFile)!);
        await File.WriteAllTextAsync(projectFile, json);

        var error = Assert.Throws<ArgumentException>(() => Configuration.Load(workspace.Root, null));
        Assert.Contains(projectFile, error.Message);
        Assert.Contains(setting, error.Message);
    }

    [Fact]
    public async Task ConcurrentDefaultCreationPreservesFingerprintAndSubsequentEdits()
    {
        using var workspace = FixtureWorkspace.Empty();
        var root = new DirectoryInfo(workspace.Root);
        var before = Configuration.Load(workspace.Root, null);
        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Configuration.CreateProjectDefaultsAsync(root, CancellationToken.None)));
        var projectFile = ProjectConfigPath(workspace);
        Assert.Equal(projectFile, Assert.Single(Directory.GetFileSystemEntries(Path.GetDirectoryName(projectFile)!)));
        Assert.Equal(before.Fingerprint, Configuration.Load(workspace.Root, null).Fingerprint);

        const string edited = """{"hotspots":{"classLines":{"lower":200,"upper":500}}}""";
        await File.WriteAllTextAsync(projectFile, edited);
        await Configuration.CreateProjectDefaultsAsync(root, CancellationToken.None);
        Assert.Equal(edited, await File.ReadAllTextAsync(projectFile));
    }
}
