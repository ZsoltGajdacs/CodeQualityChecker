using Cqc.Core;
using Cqc.CSharp;
using Cqc.Java;
using Xunit;

namespace Cqc.Tests;

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

    public Task XunitReferences(ProjectSnapshot snapshot)
    {
        var paths = PlatformReferences.Read([]).Concat([typeof(FactAttribute).Assembly.Location, typeof(Assert).Assembly.Location]).ToArray();
        var projects = snapshot.Modules.Where(module => module.Language == "csharp" && StaticProjectSettings.Read(snapshot, module).IsTestProject)
            .Select(module => new CSharpProjectReferences(module.Path, paths));
        var evidence = Directory.CreateDirectory(Path.Combine(Root, ".cqc")).FullName;
        return CSharpReferenceMetadata.WriteAsync(snapshot, evidence, projects, CancellationToken.None);
    }

    public async Task Git(params string[] arguments)
    {
        var result = await ProcessRunner.RunAsync("git", arguments, Root, CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.Error);
    }

    public IEnumerable<string> SourceFiles() => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
        .Where(path => !Path.GetRelativePath(Root, path).Split(Path.DirectorySeparatorChar)
            .Any(part => part is ".cqc" or "obj" or "bin" or "target" or "TestResults" or ".git"));

    public string SourceContents() => string.Join("\n", SourceFiles().Order(StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(Root, path) + "\0" + File.ReadAllText(path)));
    public void Dispose() => Directory.Delete(Root, true);
}
