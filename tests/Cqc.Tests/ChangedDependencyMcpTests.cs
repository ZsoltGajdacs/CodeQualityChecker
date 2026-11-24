using System.Text.Json;
using Cqc.Core;
using ModelContextProtocol.Client;
using Xunit;

namespace Cqc.Tests;

public sealed class ChangedDependencyMcpTests
{
    [Fact]
    [Trait("Category", "Execution")]
    public async Task JavaCycleUsesTheReferenceInTheSecondMethodWhenOnlyThatMethodChanges()
    {
        using var workspace = FixtureWorkspace.Copy("mixed/java");
        const string left = "src/main/java/demo/Left.java";
        const string source = """
            package demo;
            public class Left {
                static int first() {
                    return Right.value();
                }
                static int second() {
                    int value = Right.value();
                    return value + 1;
                }
            }
            """;
        await ChangedScopeTests.Write(workspace, left, source);
        await ChangedScopeTests.Write(workspace, "src/main/java/demo/Right.java", """
            package demo;
            public class Right {
                static int value() { return 1; }
                static int back() { return Left.first(); }
            }
            """);
        await ChangedScopeTests.Initialize(workspace);
        await ChangedScopeTests.Write(workspace, left, source.Replace("value + 1", "value + 2", StringComparison.Ordinal));

        await AssertChangedCycle(workspace, "java:pom.xml#main::demo.Left", [new(left, 7, 7)],
            repositoryLocation: new(left, 4, 4));
    }

    [Fact]
    [Trait("Category", "Execution")]
    public async Task CSharpCycleDeductsOnceFromEachTouchedPartialFileAndOnceFromTheClass()
    {
        using var workspace = FixtureWorkspace.Empty();
        const string first = "Left.First.cs";
        const string second = "Left.Second.cs";
        const string source = """
            namespace Demo;
            public partial class Left
            {
                public static int First()
                {
                    int value = Right.Value();
                    return value + 1;
                }
            }
            """;
        var sibling = source.Replace("First()", "Second()", StringComparison.Ordinal);
        await ChangedScopeTests.Write(workspace, "References.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        await ChangedScopeTests.Write(workspace, first, source);
        await ChangedScopeTests.Write(workspace, second, sibling);
        await ChangedScopeTests.Write(workspace, "Right.cs", """
            namespace Demo;
            public class Right
            {
                public static int Value() => 1;
                public static int Back() => Left.First();
            }
            """);
        await ChangedScopeTests.Initialize(workspace);
        await ChangedScopeTests.Write(workspace, first, source.Replace("value + 1", "value + 2", StringComparison.Ordinal));
        await ChangedScopeTests.Write(workspace, second, sibling.Replace("value + 1", "value + 3", StringComparison.Ordinal));

        await AssertChangedCycle(workspace, "csharp:References.csproj#Demo.Left",
            [new(first, 6, 6), new(second, 6, 6)], repositoryLocation: new(first, 6, 6));
    }

    private static async Task AssertChangedCycle(FixtureWorkspace workspace, string typeId, Location[] touched,
        Location repositoryLocation)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var client = await McpTests.Connect(timeout.Token);
        var changed = await Dependencies(client, "check_uncommitted_changes_quality",
            new { projectRoot = workspace.Root, execute = true }, timeout.Token);
        var finding = Assert.Single(changed.GetProperty("findings").EnumerateArray());
        Assert.Equal("dependencies.cycle", finding.GetProperty("rule").GetString());
        Assert.Equal(touched, Locations(finding));
        var findingId = finding.GetProperty("id").GetString();
        Assert.Equal(typeId, Assert.Single(finding.GetProperty("entityIds").EnumerateArray()).GetString());
        AssertScores(changed, typeId, touched.Select(location => location.File), findingId);

        var repository = await Dependencies(client, "check_dependencies", new { projectRoot = workspace.Root }, timeout.Token);
        var repositoryFinding = Assert.Single(repository.GetProperty("findings").EnumerateArray(), item =>
            item.GetProperty("rule").GetString() == "dependencies.cycle" &&
            item.GetProperty("entityIds").EnumerateArray().Any(id => id.GetString() == typeId));
        Assert.Equal(findingId, repositoryFinding.GetProperty("id").GetString());
        Assert.Equal(repositoryLocation, Assert.Single(Locations(repositoryFinding)));
    }

    private static void AssertScores(JsonElement family, string typeId, IEnumerable<string> files, string? findingId)
    {
        var entities = family.GetProperty("entities").EnumerateArray().ToArray();
        var expected = files.Select(file => "file:" + file).Prepend(typeId).Order(StringComparer.Ordinal);
        Assert.Equal(expected, entities.Select(entity => entity.GetProperty("entityId").GetString()).Order(StringComparer.Ordinal));
        Assert.All(entities, entity =>
        {
            Assert.Equal(9, entity.GetProperty("score").GetInt32());
            Assert.Equal(findingId, Assert.Single(entity.GetProperty("findingIds").EnumerateArray()).GetString());
        });
    }

    private static IEnumerable<Location> Locations(JsonElement finding) =>
        finding.GetProperty("locations").EnumerateArray().Select(location => new Location(
            location.GetProperty("file").GetString()!, location.GetProperty("startLine").GetInt32(),
            location.GetProperty("endLine").GetInt32()));

    private static async Task<JsonElement> Dependencies(McpClient client, string tool, object request, CancellationToken token)
    {
        var summary = await McpTests.Read(client, tool, request, token);
        var raw = await McpTests.Raw(client, McpTests.ReportId(summary), token);
        Assert.DoesNotContain(raw.GetProperty("diagnostics").EnumerateArray(),
            entry => entry.GetProperty("code").GetString() == "execution_failed");
        return Assert.Single(raw.GetProperty("results").EnumerateArray(), family =>
            family.GetProperty("family").GetString() == "dependencies");
    }
}
