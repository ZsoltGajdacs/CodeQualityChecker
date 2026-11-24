using System.IO.Compression;
using Cqc.Core;
using Xunit;

namespace Cqc.Tests;

public sealed class EvidenceBundleTests
{
    private const string BundleName = "evidence-v1.zip";
    private const string ManifestName = "csharp-references.json";

    [Fact]
    public async Task ReaderKeepsOneRunWhileReplacementUsesTheSameFingerprint()
    {
        using var workspace = FixtureWorkspace.Empty();
        await PublishAsync(workspace, "first");
        var diagnostics = new List<Diagnostic>();
        using var first = await EvidenceStore.OpenAsync(new(workspace.Root), diagnostics, CancellationToken.None);
        await using var oldHandle = new FileStream(BundlePath(workspace), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        await PublishAsync(workspace, "second");
        using var second = await EvidenceStore.OpenAsync(new(workspace.Root), diagnostics, CancellationToken.None);

        Assert.Empty(diagnostics);
        await AssertRunAsync(first, "first");
        await AssertRunAsync(second, "second");
        using var oldArchive = new ZipArchive(oldHandle, ZipArchiveMode.Read);
        using var oldManifest = new StreamReader(Assert.Single(oldArchive.Entries, entry => entry.Name == ManifestName).Open());
        Assert.Equal("first", await oldManifest.ReadToEndAsync());
    }

    [Fact]
    public async Task FailedOrCanceledReplacementPreservesThePublishedBundleAndCanBeRetried()
    {
        using var workspace = FixtureWorkspace.Empty();
        await PublishAsync(workspace, "first");
        var before = await File.ReadAllBytesAsync(BundlePath(workspace));

        await Assert.ThrowsAsync<FileNotFoundException>(() => PublishAsync(workspace, null));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PublishAsync(workspace, "canceled", new(true)));

        Assert.Equal(before, await File.ReadAllBytesAsync(BundlePath(workspace)));
        Assert.Equal(BundlePath(workspace), Assert.Single(Directory.GetFiles(Path.GetDirectoryName(BundlePath(workspace))!)));
        await PublishAsync(workspace, "retry");
        using var retry = await EvidenceStore.OpenAsync(new(workspace.Root), [], CancellationToken.None);
        await AssertRunAsync(retry, "retry");
    }

    [Fact]
    public async Task InvalidBundleShadowsLegacyEvidenceAndLeavesAUsableEmptyView()
    {
        using var workspace = FixtureWorkspace.Empty();
        Directory.CreateDirectory(Path.GetDirectoryName(BundlePath(workspace))!);
        var legacyCoverage = Path.Combine(workspace.Root, ".cqc", "coverage.json");
        await File.WriteAllTextAsync(legacyCoverage, "legacy");
        var diagnostics = new List<Diagnostic>();
        using var legacy = await EvidenceStore.OpenAsync(new(workspace.Root), diagnostics, CancellationToken.None);
        Assert.Equal(legacyCoverage, legacy.CoveragePath);
        Assert.Null(legacy.ReferenceDirectory);

        using (var archive = ZipFile.Open(BundlePath(workspace), ZipArchiveMode.Create))
            archive.CreateEntry(ManifestName);
        using var invalid = await EvidenceStore.OpenAsync(new(workspace.Root), diagnostics, CancellationToken.None);

        Assert.Equal("evidence_invalid", Assert.Single(diagnostics).Code);
        Assert.False(File.Exists(invalid.CoveragePath));
        Assert.Empty(Directory.GetFiles(Assert.IsType<string>(invalid.ReferenceDirectory)));
        Assert.Equal("legacy", await File.ReadAllTextAsync(legacyCoverage));
    }

    private static async Task AssertRunAsync(EvidenceSnapshot view, string expected)
    {
        var coverage = EvidenceStore.Read(await File.ReadAllTextAsync(view.CoveragePath));
        Assert.Equal("source", coverage.SourceFingerprint);
        Assert.Equal(expected, coverage.ToolVersions["run"]);
        var manifest = Path.Combine(Assert.IsType<string>(view.ReferenceDirectory), ManifestName);
        Assert.Equal(expected, await File.ReadAllTextAsync(manifest));
    }

    private static async Task PublishAsync(FixtureWorkspace workspace, string? run, CancellationToken token = default)
    {
        using var artifacts = new BuildArtifacts(Directory.CreateTempSubdirectory("cqc-evidence-test-").FullName)
        {
            Results = [new("csharp", [], new()) { ReferenceManifestName = ManifestName }]
        };
        if (run is not null) await File.WriteAllTextAsync(Path.Combine(artifacts.RunDirectory, ManifestName), run);
        await EvidenceStore.PublishRunAsync(new(workspace.Root), artifacts,
            new("2", "source", "config", true, new() { ["run"] = run ?? "missing" }, []), token);
    }

    private static string BundlePath(FixtureWorkspace workspace) => Path.Combine(workspace.Root, ".cqc", BundleName);
}
