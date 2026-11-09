using System.Text;
using Cqc.Core;
using Cqc.CSharp;
using Xunit;

namespace Cqc.Tests;

public sealed class DiscoveryPerformanceTests
{
    [Fact]
    public async Task StreamedFingerprintRetainsCanonicalContentAndExecutableModeFormat()
    {
        using var workspace = FixtureWorkspace.Empty();
        var content = Encoding.UTF8.GetBytes(new string('x', 200000) + "é🙂\n");
        await File.WriteAllBytesAsync(Path.Combine(workspace.Root, "Source.cs"), content);
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "mvnw"), "#!/bin/sh\n");
        var definition = new CSharpAnalyzer().Definition with { BuildInputPatterns = ["mvnw"], ExecutableBuildInputPatterns = ["mvnw"] };
        var expected = "Source.cs\0" + Wire.HashBytes(content) + "\nmvnw\0" + Wire.Hash("#!/bin/sh\n") + "\n";
        if (!OperatingSystem.IsWindows()) expected += "mode\0mvnw\0" + (int)File.GetUnixFileMode(Path.Combine(workspace.Root, "mvnw")) + "\n";
        var result = await ProjectDiscovery.DiscoverAsync(new() { ProjectRoot = workspace.Root }, new(), CancellationToken.None, [definition]);
        Assert.Equal(Wire.Hash(expected), result.SourceFingerprint);
    }

    [Fact]
    public async Task GitIgnoreInputPreservesUnusualNamesAcrossFormerBatchBoundaries()
    {
        using var workspace = FixtureWorkspace.Empty();
        await workspace.Git("init", "-q");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, ".gitignore"), "Ignored*.cs\n");
        for (var index = 0; index < 220; index++)
            await File.WriteAllTextAsync(Path.Combine(workspace.Root, $"Ignored{index}.cs"), "class Ignored { }");
        const string unusual = "A\nquoted name.cs";
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, unusual), "class Visible { }");
        Assert.Equal([unusual], (await workspace.Discover()).Files);
    }

    [Fact]
    public void ProjectOwnershipPreservesNearestProjectAndStableTies()
    {
        var root = new ProjectModule("csharp", "Root.csproj", "dotnet", true);
        var first = root with { Path = "nested/First.csproj" };
        var second = root with { Path = "nested/Second.csproj" };
        var index = new ProjectOwnership([root, first, second]);
        Assert.Equal(root, index.ForFile("Sibling.cs"));
        Assert.Equal(first, index.ForFile("nested/deep/Source.cs"));
        Assert.True(index.IsAmbiguous(first));
        Assert.False(index.IsAmbiguous(root));
        Assert.Null(new ProjectOwnership([first]).ForFile("nested-sibling/Source.cs"));
    }
}
