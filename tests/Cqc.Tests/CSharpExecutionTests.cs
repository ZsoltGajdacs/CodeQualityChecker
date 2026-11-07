using Cqc.Core;
using Cqc.CSharp;
using Xunit;

namespace Cqc.Tests;

public sealed class CSharpExecutionTests
{
    [Fact]
    public async Task ConflictingAssemblyImagesCannotBeCertifiedAsOneCompilation()
    {
        using var workspace = FixtureWorkspace.Empty();
        var first = Path.Combine(workspace.Root, "first", "Cqc.CSharp.dll");
        var second = Path.Combine(workspace.Root, "second", "Cqc.CSharp.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        File.Copy(typeof(CSharpAnalyzer).Assembly.Location, first);
        File.Copy(typeof(CSharpAnalyzer).Assembly.Location, second);
        await using (var appended = new FileStream(second, FileMode.Append)) await appended.WriteAsync(new byte[] { 1 });
        var snapshot = new ProjectSnapshot(workspace.Root, [], [], [], "source", []);
        await Assert.ThrowsAsync<InvalidOperationException>(() => CSharpReferenceMetadata.WriteAsync(snapshot, workspace.Root,
            [first, second], CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(workspace.Root, ".cqc", "csharp-references.txt")));
    }

    [Fact]
    public async Task NewReferenceSetReplacesRemovedReferencesAndPersistsScratchAssemblies()
    {
        using var workspace = FixtureWorkspace.Empty();
        var metadata = Path.Combine(workspace.Root, ".cqc");
        Directory.CreateDirectory(metadata);
        await File.WriteAllTextAsync(Path.Combine(metadata, "csharp-references.txt"), "/old/reference.dll\n");
        var assembly = Path.Combine(workspace.Root, "Cqc.CSharp.dll");
        File.Copy(typeof(CSharpAnalyzer).Assembly.Location, assembly);
        var snapshot = new ProjectSnapshot(workspace.Root, [], [], [], "source", []);
        await CSharpReferenceMetadata.WriteAsync(snapshot, workspace.Root, [assembly], CancellationToken.None);
        var reference = Assert.Single(await File.ReadAllLinesAsync(Path.Combine(metadata, "csharp-references.txt")));
        Assert.StartsWith(Path.Combine(metadata, "references"), reference, StringComparison.Ordinal);
        Assert.True(File.Exists(reference));
        Assert.Equal("source", await File.ReadAllTextAsync(Path.Combine(metadata, "csharp-references.txt.source")));
    }
}
