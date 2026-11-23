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
            [new("First.csproj", [first, second])], CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(workspace.Root, ".cqc", CSharpReferenceMetadata.FileName)));
        await CSharpReferenceMetadata.WriteAsync(snapshot, workspace.Root,
            [new("First.csproj", [first]), new("Second.csproj", [second])], CancellationToken.None);
        var diagnostics = new List<Diagnostic>();
        var manifest = CSharpReferenceMetadata.Read(snapshot, diagnostics);
        Assert.Empty(diagnostics);
        Assert.NotNull(manifest);
        Assert.Equal(["First.csproj", "Second.csproj"], manifest.Projects.Select(project => project.ProjectPath));
        Assert.NotEqual(manifest.Projects[0].Paths[0], manifest.Projects[1].Paths[0]);
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
        var diagnostics = new List<Diagnostic>();
        Assert.Null(CSharpReferenceMetadata.Read(snapshot, diagnostics));
        Assert.Equal("metadata-stale", Assert.Single(diagnostics).Code);
        diagnostics.Clear();
        await CSharpReferenceMetadata.WriteAsync(snapshot, workspace.Root, [new("Project.csproj", [assembly])], CancellationToken.None);
        var manifest = CSharpReferenceMetadata.Read(snapshot, diagnostics);
        Assert.Empty(diagnostics);
        Assert.NotNull(manifest);
        var reference = Assert.Single(Assert.Single(manifest.Projects).Paths);
        Assert.StartsWith(Path.Combine(metadata, "references"), reference, StringComparison.Ordinal);
        Assert.True(File.Exists(reference));
        Assert.Equal("source", manifest.SourceFingerprint);
    }

    [Fact]
    [Trait("Category", "Execution")]
    public async Task CoverageDistinguishesGenericOverloadsAndIndependentProjects()
    {
        using var workspace = FixtureWorkspace.Copy("mixed/csharp");
        var sample = Path.Combine(workspace.Root, "Quality.Sample");
        var tests = Path.Combine(workspace.Root, "Quality.Sample.Tests");
        var independent = Directory.CreateDirectory(Path.Combine(workspace.Root, "Independent")).FullName;
        const string counter = """
            namespace Quality.Sample;
            public class Counter
            {
                public int Run() => 4; public int Run<T>() => 9;
                public int Measure(bool value) => value ? 4 : 5;
                public int Measure<T>(bool value) => value ? 9 : 10;
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(sample, "Counter.cs"), counter);
        await File.WriteAllTextAsync(Path.Combine(independent, "Counter.cs"), counter);
        await File.WriteAllTextAsync(Path.Combine(independent, "Independent.csproj"),
            "<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(tests, "CounterTests.cs"),
            """
            namespace Quality.Sample.Tests;
            public class CounterTests
            {
                [Xunit.Fact] public void Generic() => Xunit.Assert.Equal(9, new Counter().Run<int>());
                [Xunit.Fact] public void GenericBranch() => Xunit.Assert.Equal(9, new Counter().Measure<int>(true));
            }
            """);

        var result = await new AnalysisService([new CSharpAnalyzer()]).AnalyzeAsync(new()
            { ProjectRoot = workspace.Root, Execute = true, TimeoutSeconds = 120 }, ["code"], CancellationToken.None);
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "execution_failed");
        var counters = Assert.Single(result.Results).Entities.Where(item => item.EntityId.EndsWith("#Quality.Sample.Counter", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, counters.Length);
        var sampleCounter = Assert.Single(counters, item => item.File == "Quality.Sample/Counter.cs");
        Assert.Null(sampleCounter.Metrics["method:" + sampleCounter.EntityId + ".Run():coverage"]);
        Assert.Null(sampleCounter.Metrics["method:" + sampleCounter.EntityId + ".Run``1():coverage"]);
        Assert.Equal(0, sampleCounter.Metrics["method:" + sampleCounter.EntityId + ".Measure(System.Boolean):coverage"]);
        Assert.Equal(0.5, sampleCounter.Metrics["method:" + sampleCounter.EntityId + ".Measure``1(System.Boolean):coverage"]);
        var independentCounter = Assert.Single(counters, item => item.File == "Independent/Counter.cs");
        Assert.Null(independentCounter.Metrics["method:" + independentCounter.EntityId + ".Run``1():coverage"]);
    }
}
