using Cqc.Core;
using Cqc.CSharp;
using Xunit;

namespace Cqc.Tests;

public sealed class GitHotspotHistoryTests
{
    [Fact]
    public async Task CountsAnEditAtTheCutoffThroughABackdatedRenameIntoTheProject()
    {
        using var workspace = FixtureWorkspace.Empty();
        await workspace.Git("init", "-qb", "main");
        await WriteClass(workspace, "Original.cs", 1);
        await Commit(workspace, "2024-01-01T12:00:00Z");
        await WriteClass(workspace, "Original.cs", 2);
        await Commit(workspace, "2025-11-25T11:59:59Z");
        await WriteClass(workspace, "Original.cs", 3);
        await Commit(workspace, "2025-11-25T12:00:00Z");
        var project = Directory.CreateDirectory(Path.Combine(workspace.Root, "project")).FullName;
        await workspace.Git("mv", "Original.cs", "project/Renamed file.cs");
        await Commit(workspace, "2024-11-01T12:00:00Z");
        await Commit(workspace, "2026-11-25T12:00:00Z");

        var report = await Analyze(project);
        var window = Assert.IsType<HotspotHistory>(report.HotspotHistory);
        Assert.Equal(new DateTimeOffset(2025, 11, 25, 12, 0, 0, TimeSpan.Zero), window.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero), window.ThroughUtc);
        var entity = Class(report);
        Assert.Equal("Renamed file.cs", entity.File);
        Assert.Equal(1, entity.Metrics["editCommits"]);
    }

    [Fact]
    public async Task CountsTheNetMergeEditOnceAndExcludesUnmergedBranches()
    {
        using var workspace = FixtureWorkspace.Empty();
        await workspace.Git("init", "-qb", "main");
        await WriteClass(workspace, "Example.cs", 1);
        await Commit(workspace, "2026-01-01T12:00:00Z");
        await workspace.Git("switch", "-qc", "topic");
        await WriteClass(workspace, "Example.cs", 2);
        await Commit(workspace, "2026-02-01T12:00:00Z");
        await WriteClass(workspace, "Example.cs", 3);
        await Commit(workspace, "2026-03-01T12:00:00Z");
        await workspace.Git("switch", "-q", "main");
        await DatedGit(workspace, "2026-04-01T12:00:00Z", "merge", "--no-ff", "-qm", "merge topic", "topic");
        await workspace.Git("switch", "-qc", "unmerged");
        await WriteClass(workspace, "Example.cs", 4);
        await Commit(workspace, "2026-05-01T12:00:00Z");
        await workspace.Git("switch", "-q", "main");

        var entity = Class(await Analyze(workspace.Root));
        Assert.Equal(1, entity.Metrics["editCommits"]);
    }

    [Fact]
    public async Task StopsAtFileRecreationAndIgnoresDirtyEdits()
    {
        using var workspace = FixtureWorkspace.Empty();
        await workspace.Git("init", "-qb", "main");
        await WriteClass(workspace, "Example.cs", 1);
        await Commit(workspace, "2026-01-01T12:00:00Z");
        await WriteClass(workspace, "Example.cs", 2);
        await Commit(workspace, "2026-02-01T12:00:00Z");
        await workspace.Git("rm", "Example.cs");
        await Commit(workspace, "2026-03-01T12:00:00Z");
        await WriteClass(workspace, "Example.cs", 3);
        await Commit(workspace, "2026-04-01T12:00:00Z");
        await WriteClass(workspace, "Example.cs", 4);
        await Commit(workspace, "2026-05-01T12:00:00Z");
        await WriteClass(workspace, "Example.cs", 5);

        var entity = Class(await Analyze(workspace.Root));
        Assert.Equal(1, entity.Metrics["editCommits"]);
    }

    [Fact]
    public async Task CountsOneCommitAcrossPartialDeclarationsAndKeepsTheUnselectedPart()
    {
        using var workspace = FixtureWorkspace.Empty();
        await workspace.Git("init", "-qb", "main");
        await ChangedScopeTests.Write(workspace, "A.cs", "partial class Example { int Left() => 1; }\n");
        await ChangedScopeTests.Write(workspace, "B.cs", "partial class Example { }\n");
        await Commit(workspace, "2026-01-01T12:00:00Z");
        await ChangedScopeTests.Write(workspace, "A.cs", "partial class Example { int Left() => 2; }\n");
        await ChangedScopeTests.Write(workspace, "B.cs", """
            partial class Example
            {
                public Example(int a, int b, int c, int d, int e, int f, int g, int h) { }
            }
            """);
        await Commit(workspace, "2026-02-01T12:00:00Z");

        var report = await new AnalysisService([new CSharpAnalyzer()]).AnalyzeHotspotsAsync(
            new() { ProjectRoot = workspace.Root, Scope = "paths", Paths = ["A.cs"] }, CancellationToken.None);
        var entity = Class(report);
        Assert.Equal(1, entity.Metrics["editCommits"]);
        Assert.Equal(8, entity.Metrics["constructorParameters"]);
        Assert.Equal(5, entity.Metrics["classLines"]);
        var file = Assert.Single(Assert.Single(report.Results).Entities, item => item.Kind == "file");
        Assert.Equal("A.cs", file.File);
        Assert.Equal(9, file.Score);
    }

    [Fact]
    public async Task ReportsShallowHistoryAsIncomplete()
    {
        using var source = FixtureWorkspace.Empty();
        await source.Git("init", "-qb", "main");
        await WriteClass(source, "Example.cs", 1);
        await Commit(source, "2026-01-01T12:00:00Z");
        await WriteClass(source, "Example.cs", 2);
        await Commit(source, "2026-02-01T12:00:00Z");
        using var parent = FixtureWorkspace.Empty();
        await parent.Git("clone", "-q", "--depth=1", new Uri(source.Root).AbsoluteUri, "clone");

        var report = await Analyze(Path.Combine(parent.Root, "clone"));
        var family = Assert.Single(report.Results);
        Assert.Equal("incomplete", family.Status);
        Assert.Contains(family.Diagnostics, diagnostic => diagnostic.Code == "hotspots.history_unavailable");
        Assert.All(family.Entities, entity =>
        {
            Assert.Null(entity.Score);
            Assert.Contains("hotspots.history_unavailable", entity.Blockers);
        });
        Assert.Contains(family.Entities, entity => entity.Kind == "class");
    }

    private static Task<AnalysisResponse> Analyze(string root) => new AnalysisService([new CSharpAnalyzer()])
        .AnalyzeHotspotsAsync(new() { ProjectRoot = root }, CancellationToken.None);

    private static EntityScore Class(AnalysisResponse report)
    {
        var family = Assert.Single(report.Results);
        Assert.Equal("complete", family.Status);
        return Assert.Single(family.Entities, entity => entity.Kind == "class");
    }

    private static Task WriteClass(FixtureWorkspace workspace, string path, int value) =>
        ChangedScopeTests.Write(workspace, path, $"class Example {{ int Value() => {value}; }}\n");

    private static async Task Commit(FixtureWorkspace workspace, string date)
    {
        await workspace.Git("add", "-A");
        await DatedGit(workspace, date, "commit", "--allow-empty", "-qm", "fixture");
    }

    private static async Task DatedGit(FixtureWorkspace workspace, string date, params string[] arguments)
    {
        var result = await ProcessRunner.RunAsync(new ProcessCommand
        {
            Executable = "git",
            Directory = workspace.Root,
            Arguments = ["-c", "user.email=tests@example.invalid", "-c", "user.name=CQC Tests", .. arguments],
            EnvironmentOverrides = new Dictionary<string, string> { ["GIT_AUTHOR_DATE"] = date, ["GIT_COMMITTER_DATE"] = date }
        }, CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.Error);
    }
}
