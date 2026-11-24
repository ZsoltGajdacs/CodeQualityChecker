using Cqc.Core;
using Cqc.CSharp;
using Cqc.Java;
using Xunit;

namespace Cqc.Tests;

public sealed class DuplicationLineTests
{
    [Theory]
    [InlineData("cs", false, 4, 2)]
    [InlineData("java", false, 4, 2)]
    [InlineData("cs", true, 4, 2)]
    [InlineData("java", true, 4, 2)]
    [InlineData("cs", true, 1, 2)]
    [InlineData("java", true, 1, 2)]
    [InlineData("cs", false, 5, 0)]
    [InlineData("java", false, 5, 0)]
    public async Task OnlyBodiesMeetingTheCodeLineMinimumCanJoinAGroup(
        string extension, bool sourceOnly, int minimum, int groupSize)
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, ".cqc.json"),
            $$"""{"duplicateMinNodes": 1, "duplicateMinLines": {{minimum}}}""");
        foreach (var name in new[] { "Left", "Right", "Short" })
        {
            var continued = name == "Short" ? " " : "\n";
            await File.WriteAllTextAsync(Path.Combine(workspace.Root, $"{name}.{extension}"), $$"""
                class {{name}} {
                    int Work(
                        int input)
                    {
                        int value = input * 2;

                        // Count code lines only.
                        value = value +{{continued}}5;
                        return value;
                    }
                }
                """);
        }

        var family = await Duplication(workspace, extension, sourceOnly);
        var classes = family.Entities.Where(entity => entity.Kind == "class").ToArray();
        Assert.Equal(3, classes.Length);
        var shortBody = Assert.Single(classes, entity => entity.File == $"Short.{extension}");
        Assert.Equal(10, shortBody.Score);
        Assert.Equal(0, shortBody.Metrics["eligibleExecutables"]);
        var locations = family.Findings.SelectMany(finding => finding.Locations).Select(location => location.File).ToArray();
        Assert.Equal(groupSize == 0 ? [] : new[] { $"Left.{extension}", $"Right.{extension}" }, locations);
        Assert.Equal(groupSize == 0 ? 0 : 1, family.Findings.Length);
    }

    [Theory]
    [InlineData("cs", "int Work() =>\n1 +\n2 +\n3 +\n4;", 1)]
    [InlineData("cs", "int Work() =>\n1 +\n2 +\n3 + 4;", 0)]
    [InlineData("java", "int field;\n{\nint value = 1;\nvalue = value +\n2;\nfield = value;\n}", 1)]
    [InlineData("java", "int field;\n{\nint value = 1;\nvalue = value + 2;\nfield = value;\n}", 0)]
    [InlineData("cs", "int Work() {\nint Local() =>\n1 +\n2 +\n3 +\n4;\nint x = Local();\nx += 1;\nreturn x;\n}", 1)]
    [InlineData("java", "int field;\n{\nRunnable action = () -> {\nint x = 1;\nx = x +\n2;\nfield = x;\n}; action.run();\nfield = 0;\n}", 1)]
    public async Task ExpressionAndInitializerCodeLinesFollowExecutableOwnership(string extension, string members, int eligible)
    {
        using var workspace = FixtureWorkspace.Empty();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, ".cqc.json"), """{"duplicateMinNodes": 1}""");
        foreach (var name in new[] { "Left", "Right" })
            await File.WriteAllTextAsync(Path.Combine(workspace.Root, $"{name}.{extension}"), $"class {name} {{\n{members}\n}}");

        var family = await Duplication(workspace, extension, sourceOnly: true);
        Assert.Equal(eligible, family.Findings.Length);
        var classes = family.Entities.Where(entity => entity.Kind == "class").ToArray();
        Assert.Equal(2, classes.Length);
        Assert.All(classes, entity => Assert.Equal(eligible, entity.Metrics["eligibleExecutables"]));
    }

    private static async Task<FamilyResult> Duplication(FixtureWorkspace workspace, string extension, bool sourceOnly)
    {
        ILanguageAnalyzer analyzer = extension == "cs" ? new CSharpAnalyzer() : new JavaAnalyzer();
        var service = new AnalysisService([analyzer]);
        var request = new AnalysisRequest { ProjectRoot = workspace.Root };
        var report = sourceOnly
            ? await service.ReviewAsync(request, CancellationToken.None)
            : await service.AnalyzeAsync(request, ["duplication"], CancellationToken.None);
        return Assert.Single(report.Results, result => result.Family == "duplication");
    }
}
