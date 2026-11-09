using System.Diagnostics;
using Cqc.Core;
using Cqc.CSharp;
using Cqc.Java;
using Xunit;

namespace Cqc.Tests;

public sealed class IntegrationTests
{
    [Fact]
    public async Task DiscoveryFindsEveryLanguageAndGeneratedCodeIsExcluded()
    {
        using var workspace = FixtureWorkspace.Copy("mixed");
        var snapshot = await workspace.Discover();
        AssertMixedModules(snapshot);
        Assert.Contains(snapshot.Files, x => x.EndsWith("Calculator.cs", StringComparison.Ordinal));
        Assert.Contains(snapshot.Files, x => x.EndsWith("Calculator.java", StringComparison.Ordinal));
        Assert.DoesNotContain(snapshot.Files, x => x.EndsWith("Generated.g.cs", StringComparison.Ordinal));
        var inspection = await Service().InspectAsync(new() { ProjectRoot = workspace.Root }, CancellationToken.None);
        Assert.Equal(Wire.Target, inspection.Message);
    }

    [Fact]
    public async Task SelectedFileUsesOtherFilesForDuplicationAndCyclesWithoutExecutingBuilds()
    {
        using var workspace = FixtureWorkspace.Copy("mixed/csharp");
        var before = workspace.SourceContents();
        var request = new AnalysisRequest { ProjectRoot = workspace.Root, Scope = "paths", Paths = ["Quality.Sample/Left.cs"] };
        var result = await Service().AnalyzeAsync(request, ["code", "duplication", "dependencies"], CancellationToken.None);
        Assert.Equal(["Quality.Sample/Left.cs"], result.AnalyzedFiles);
        var duplication = Assert.Single(result.Results, x => x.Family == "duplication");
        Assert.Contains(duplication.Findings, x => x.Locations.Any(l => l.File.EndsWith("Right.cs", StringComparison.Ordinal)));
        var dependencies = Assert.Single(result.Results, x => x.Family == "dependencies");
        Assert.Contains(dependencies.Findings, x => x.Rule == "dependencies.cycle");
        Assert.All(Assert.Single(result.Results, x => x.Family == "code").Entities, x => Assert.Null(x.Score));
        AssertReadOnly(workspace, before);
    }

    [Fact]
    public async Task ChangedScopeIncludesStagedUnstagedAndUntrackedFilesWithContext()
    {
        using var workspace = FixtureWorkspace.Copy("mixed/csharp");
        await workspace.Git("init", "-q");
        await workspace.Git("add", ".");
        await workspace.Git("-c", "user.email=tests@example.invalid", "-c", "user.name=CQC Tests", "commit", "-qm", "fixture");
        await File.AppendAllTextAsync(Path.Combine(workspace.Root, "Quality.Sample", "Left.cs"), "\n// staged\n");
        await workspace.Git("add", "Quality.Sample/Left.cs");
        await File.AppendAllTextAsync(Path.Combine(workspace.Root, "Quality.Sample", "Right.cs"), "\n// unstaged\n");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "New.cs"), "namespace Quality.Sample; class New { }");
        var snapshot = await workspace.Discover("changed");
        Assert.Equal(new[] { "New.cs", "Quality.Sample/Left.cs", "Quality.Sample/Right.cs" }, snapshot.SelectedFiles);
        Assert.Contains(snapshot.Files, x => x.EndsWith("Calculator.cs", StringComparison.Ordinal));
        var based = await ProjectDiscovery.DiscoverAsync(new() { ProjectRoot = workspace.Root, Scope = "changed", GitBase = "HEAD" }, new(), CancellationToken.None, FixtureWorkspace.Definitions);
        Assert.Equal(snapshot.SelectedFiles, based.SelectedFiles);
    }

    [Fact]
    public async Task GitIgnoreAndInvalidPathsAreHandledAtDiscoveryBoundary()
    {
        using var workspace = FixtureWorkspace.Empty();
        await workspace.Git("init", "-q");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, ".gitignore"), "Ignored.cs\n");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Ignored.cs"), "class Ignored {}");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Visible.cs"), "class Visible {}");
        Assert.Equal(["Visible.cs"], (await workspace.Discover()).Files);
        await Assert.ThrowsAsync<ArgumentException>(() => ProjectDiscovery.DiscoverAsync(new()
            { ProjectRoot = workspace.Root, Scope = "paths", Paths = ["../outside"] }, new(), CancellationToken.None, FixtureWorkspace.Definitions));
    }

    [Fact]
    public async Task ResultsAreDeterministicAndUnmodeledSetupExplainsIncompleteTestScore()
    {
        using var workspace = FixtureWorkspace.Copy("mixed/csharp");
        await workspace.XunitReferences(await workspace.Discover());
        var request = new AnalysisRequest { ProjectRoot = workspace.Root };
        var service = Service();
        var first = await service.AnalyzeAsync(request, ["tests", "duplication"], CancellationToken.None);
        var second = await service.AnalyzeAsync(request, ["tests", "duplication"], CancellationToken.None);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first, Wire.Json), System.Text.Json.JsonSerializer.Serialize(second, Wire.Json));
        AssertWeakTestExplained(first);
    }

    private static void AssertWeakTestExplained(AnalysisResponse first)
    {
        var tests = Assert.Single(first.Results, x => x.Family == "tests");
        Assert.DoesNotContain(tests.Findings, finding => finding.Rule == "tests.noAssertion");
        var entity = Assert.Single(tests.Entities, x => x.Kind == "class");
        Assert.Null(entity.Score);
        Assert.Contains("syntax.incomplete", entity.Blockers); // Unmodeled field initialization is explicit evidence debt.
    }

    [Fact]
    public async Task ProcessExecutionSupportsCancellationAndPreservesArgumentBoundaries()
    {
        using var workspace = FixtureWorkspace.Empty();
        var echoed = await ProcessRunner.RunAsync("/usr/bin/printf", ["%s", "literal $(not-executed); spaces"], workspace.Root, CancellationToken.None);
        Assert.Equal("literal $(not-executed); spaces", echoed.Output);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProcessRunner.RunAsync("/bin/sleep", ["30"], workspace.Root, timeout.Token));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task UnsupportedProjectsAndLanguagesAreExplicitlyReported()
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Legacy.csproj"), "<Project><PropertyGroup /></Project>");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Legacy.cs"), "class Legacy { public int Read() => 1; }");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "other.go"), "package other");
        var snapshot = await workspace.Discover();
        Assert.Contains(snapshot.Diagnostics, x => x.Code == "unsupported_language");
        Assert.Contains(snapshot.Modules, x => !x.Supported && x.Integration == "legacy-dotnet");
        var result = await Service().AnalyzeAsync(new() { ProjectRoot = workspace.Root, Execute = true }, ["code"], CancellationToken.None);
        Assert.Contains(result.Diagnostics, x => x.Code == "execution_failed");
        Assert.All(Assert.Single(result.Results).Entities, x => Assert.Null(x.Score));
    }

    [Fact]
    [Trait("Category", "Execution")]
    public async Task FailedRealTestExecutionNeverProducesSuccessfulCoverageEvidence()
    {
        using var workspace = FixtureWorkspace.Copy("mixed/csharp");
        var file = Path.Combine(workspace.Root, "Quality.Sample.Tests", "CalculatorTests.cs");
        await File.WriteAllTextAsync(file, File.ReadAllText(file).Replace("Assert.Equal(value + 1", "Assert.Equal(value + 99", StringComparison.Ordinal));
        var before = workspace.SourceContents();
        var result = await Service().AnalyzeAsync(new() { ProjectRoot = workspace.Root, Execute = true, TimeoutSeconds = 120 }, ["code"], CancellationToken.None);
        Assert.Contains(result.Diagnostics, x => x.Code == "execution_failed");
        Assert.All(Assert.Single(result.Results).Entities, x => Assert.Null(x.Score));
        Assert.False(File.Exists(Path.Combine(workspace.Root, ".cqc", "coverage.json")));
        Assert.Equal(before, workspace.SourceContents());
    }

    [Fact]
    [Trait("Category", "Execution")]
    public async Task ExplicitMixedBuildCollectsCoverageAndKeepsOriginalSourceUnchanged()
    {
        using var workspace = FixtureWorkspace.Copy("mixed");
        var before = workspace.SourceContents();
        var result = await Service().AnalyzeAsync(new() { ProjectRoot = workspace.Root, Execute = true, TimeoutSeconds = 300 },
            ["code", "tests", "duplication", "dependencies"], CancellationToken.None);
        Assert.True(result.Diagnostics.All(x => x.Code != "execution_failed"), System.Text.Json.JsonSerializer.Serialize(result, Wire.Json));
        var code = Assert.Single(result.Results, x => x.Family == "code");
        AssertBothLanguagesScored(code);
        Assert.All(result.Results, family => Assert.Equal(family.Family is "tests" or "duplication" or "dependencies" ? "incomplete" : "complete", family.Status));
        Assert.Equal(before, workspace.SourceContents());
        Assert.True(File.Exists(Path.Combine(workspace.Root, ".cqc", "coverage.json")));
        var readOnly = await Service().AnalyzeAsync(new() { ProjectRoot = workspace.Root }, ["code"], CancellationToken.None);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(code, Wire.Json),
            System.Text.Json.JsonSerializer.Serialize(Assert.Single(readOnly.Results), Wire.Json));
    }

    private static void AssertMixedModules(ProjectSnapshot snapshot)
    {
        Assert.Contains(snapshot.Modules, x => x.Language == "java" && x.Supported);
        Assert.Equal(2, snapshot.Modules.Count(x => x.Language == "csharp" && x.Supported));
    }

    private static void AssertReadOnly(FixtureWorkspace workspace, string before)
    {
        Assert.Equal(before, workspace.SourceContents());
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, "Quality.Sample", "obj")));
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".cqc")));
    }

    private static void AssertBothLanguagesScored(FamilyResult code)
    {
        var details = System.Text.Json.JsonSerializer.Serialize(code, Wire.Json);
        Assert.True(code.Entities.Any(x => x.EntityId == "java:java/pom.xml#main::demo.Calculator" && x.Score.HasValue), details);
        Assert.True(code.Entities.Any(x => x.EntityId == "csharp:Quality.Sample.Calculator" && x.Score.HasValue), details);
        var java = Assert.Single(code.Entities, x => x.EntityId == "java:java/pom.xml#main::demo.Calculator");
        var csharp = Assert.Single(code.Entities, x => x.EntityId == "csharp:Quality.Sample.Calculator");
        Assert.Equal(1, java.Metrics["method:java:java/pom.xml#main::demo.Calculator.choose(int):coverage"]);
        Assert.Equal(1, csharp.Metrics["method:csharp:Quality.Sample.Calculator.Choose(System.Int32):coverage"]);
    }

    private static AnalysisService Service() => new([new CSharpAnalyzer(), new JavaAnalyzer()]);
}

internal sealed class FixtureWorkspace : IDisposable
{
    public static LanguageDefinition[] Definitions => [new CSharpAnalyzer().Definition, new JavaAnalyzer().Definition];
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "cqc-test-" + Guid.NewGuid().ToString("N"));
    private FixtureWorkspace() => Directory.CreateDirectory(Root);
    public static FixtureWorkspace Empty() => new();
    public static FixtureWorkspace Copy(string relative)
    {
        var workspace = new FixtureWorkspace();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "fixtures"))) directory = directory.Parent;
        if (directory is null) throw new DirectoryNotFoundException("Cannot locate CQC test fixtures.");
        CopyFiles(Path.Combine(directory.FullName, "fixtures", relative), workspace.Root);
        return workspace;
    }

    private static void CopyFiles(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source).Where(x => Path.GetFileName(x) is not ("bin" or "obj" or "target" or ".cqc")))
        {
            var target = Path.Combine(destination, Path.GetFileName(directory));
            Directory.CreateDirectory(target);
            CopyFiles(directory, target);
        }
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
    }

    public Task<ProjectSnapshot> Discover(string scope = "repository") => ProjectDiscovery.DiscoverAsync(new()
        { ProjectRoot = Root, Scope = scope }, new(), CancellationToken.None, Definitions);

    public async Task XunitReferences(ProjectSnapshot snapshot)
    {
        var path = Path.Combine(Root, ".cqc", "csharp-references.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllLinesAsync(path, new[] { typeof(FactAttribute).Assembly.Location, typeof(Assert).Assembly.Location });
        await File.WriteAllTextAsync(path + ".source", snapshot.SourceFingerprint);
    }

    public async Task Git(params string[] arguments)
    {
        var result = await ProcessRunner.RunAsync("git", arguments, Root, CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.Error);
    }

    public string SourceContents() => string.Join("\n", Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
        .Where(x => !Path.GetRelativePath(Root, x).Split(Path.DirectorySeparatorChar).Any(p => p is ".cqc" or "obj" or "bin" or "target" or "TestResults" or ".git"))
        .Order(StringComparer.Ordinal).Select(x => Path.GetRelativePath(Root, x) + "\0" + File.ReadAllText(x)));
    public void Dispose() => Directory.Delete(Root, true);
}
