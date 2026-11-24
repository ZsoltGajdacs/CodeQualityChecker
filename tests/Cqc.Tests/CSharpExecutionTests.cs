using Cqc.Core;
using Cqc.CSharp;
using System.Security.Cryptography;
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
        var evidence = Directory.CreateDirectory(Path.Combine(workspace.Root, ".cqc")).FullName;
        await Assert.ThrowsAsync<InvalidOperationException>(() => CSharpReferenceMetadata.WriteAsync(snapshot, evidence,
            [new("First.csproj", [first, second])], CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(evidence, CSharpReferenceMetadata.FileName)));
        await CSharpReferenceMetadata.WriteAsync(snapshot, evidence,
            [new("First.csproj", [first]), new("Second.csproj", [second])], CancellationToken.None);
        var diagnostics = new List<Diagnostic>();
        var manifest = CSharpReferenceMetadata.Read(snapshot, diagnostics);
        Assert.Empty(diagnostics);
        Assert.NotNull(manifest);
        Assert.Equal(["First.csproj", "Second.csproj"], manifest.Projects.Select(project => project.ProjectPath));
        Assert.NotEqual(manifest.Projects[0].References[0].Sha256, manifest.Projects[1].References[0].Sha256);
    }

    [Fact]
    public async Task LegacyManifestIsRejectedEvenWithMatchingSourceFingerprint()
    {
        using var workspace = FixtureWorkspace.Empty();
        var metadata = Path.Combine(workspace.Root, ".cqc");
        Directory.CreateDirectory(metadata);
        var snapshot = new ProjectSnapshot(workspace.Root, [], [], [], "source", []);
        await File.WriteAllTextAsync(Path.Combine(metadata, CSharpReferenceMetadata.FileName), """
            {"schemaVersion":"1","sourceFingerprint":"source","projects":[{"projectPath":"Project.csproj","paths":["/old/reference.dll"]}]}
            """);
        var diagnostics = new List<Diagnostic>();
        Assert.Null(CSharpReferenceMetadata.Read(snapshot, diagnostics));
        Assert.Equal("metadata-stale", Assert.Single(diagnostics).Code);
    }

    [Fact]
    [Trait("Category", "Execution")]
    public async Task ExecutionUsesExistingProjectAssembliesAndDetectsChangesOnReplay()
    {
        using var workspace = FixtureWorkspace.Copy("mixed/csharp");
        var localOutput = Directory.CreateDirectory(Path.Combine(workspace.Root, "bin", "existing")).FullName;
        var assembly = Path.Combine(localOutput, "Cqc.CSharp.dll");
        File.Copy(typeof(CSharpAnalyzer).Assembly.Location, assembly);
        var project = Path.Combine(workspace.Root, "Quality.Sample", "Quality.Sample.csproj");
        var xml = (await File.ReadAllTextAsync(project)).Replace("</Project>", """
            <ItemGroup><Reference Include="Cqc.CSharp"><HintPath>../bin/existing/Cqc.CSharp.dll</HintPath></Reference></ItemGroup></Project>
            """, StringComparison.Ordinal);
        await File.WriteAllTextAsync(project, xml);

        var request = new AnalysisRequest { ProjectRoot = workspace.Root, Execute = true, TimeoutSeconds = 120 };
        var service = new AnalysisService([new CSharpAnalyzer()]);
        var first = await service.AnalyzeAsync(request, ["code"], CancellationToken.None);
        Assert.DoesNotContain(first.Diagnostics, item => item.Code is "execution_failed" or "metadata-invalid");
        using var savedEvidence = await EvidenceStore.OpenAsync(new(workspace.Root), [], CancellationToken.None);
        var snapshot = (await workspace.Discover()) with { ReferenceEvidenceDirectory = savedEvidence.ReferenceDirectory };
        var manifest = CSharpReferenceMetadata.Read(snapshot, []);
        Assert.NotNull(manifest);
        Assert.Equal("2", manifest.SchemaVersion);
        var library = Assert.Single(manifest.Projects, item => item.ProjectPath == "Quality.Sample/Quality.Sample.csproj");
        var reference = Assert.Single(library.References, item => Path.GetFileName(item.Path) == "Cqc.CSharp.dll");
        Assert.EndsWith(Path.Combine(Path.GetFileName(workspace.Root), "bin", "existing", "Cqc.CSharp.dll"),
            reference.Path, StringComparison.Ordinal);
        var hash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(assembly)));
        Assert.Equal(hash, reference.Sha256);
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".cqc", "references")));

        var buildOutput = Path.Combine(workspace.Root, "Quality.Sample", "obj", "Debug", "net10.0", "Quality.Sample.dll");
        Assert.True(File.Exists(buildOutput));
        var lastWrite = File.GetLastWriteTimeUtc(buildOutput);
        var legacyCache = Directory.CreateDirectory(Path.Combine(workspace.Root, ".cqc", "references"));
        var legacyFile = Path.Combine(legacyCache.FullName, "old.dll");
        await File.WriteAllTextAsync(legacyFile, "old cache");
        var second = await service.AnalyzeAsync(request, ["code"], CancellationToken.None);
        Assert.DoesNotContain(second.Diagnostics, item => item.Code is "execution_failed" or "metadata-invalid");
        Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(buildOutput));
        Assert.Equal("old cache", await File.ReadAllTextAsync(Assert.Single(legacyCache.GetFiles()).FullName));

        var analysisOnly = request with { Execute = false };
        var replay = await service.AnalyzeAsync(analysisOnly, ["code"], CancellationToken.None);
        Assert.DoesNotContain(replay.Diagnostics, item => item.Code is "metadata-stale" or "metadata-invalid");
        await using (var stream = new FileStream(assembly, FileMode.Append)) await stream.WriteAsync(new byte[] { 1 });
        var changed = await service.AnalyzeAsync(analysisOnly, ["code"], CancellationToken.None);
        Assert.Contains(changed.Diagnostics, item => item.Code == "metadata-invalid");
        File.Delete(assembly);
        var missing = await service.AnalyzeAsync(analysisOnly, ["code"], CancellationToken.None);
        Assert.Contains(missing.Diagnostics, item => item.Code == "metadata-invalid");
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
