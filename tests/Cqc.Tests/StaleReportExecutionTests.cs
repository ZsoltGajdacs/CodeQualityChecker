using Cqc.Core;
using Cqc.CSharp;
using Xunit;

namespace Cqc.Tests;

public sealed class StaleReportExecutionTests
{
    [Fact]
    [Trait("Category", "Execution")]
    public async Task InPlaceExecutionIgnoresExistingOpenCoverReports()
    {
        using var workspace = FixtureWorkspace.Copy("mixed/csharp");
        var cached = Path.Combine(workspace.Root, "old-reports", "coverage.opencover.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
        await File.WriteAllTextAsync(cached, """
            <CoverageSession><Modules><Module><Files><File uid="1" fullPath="/old/Calculator.cs" /></Files><Classes><Class>
            <FullName>Quality.Sample.Calculator</FullName><Methods><Method>
            <Name>System.Int32 Quality.Sample.Calculator::Choose(System.String)</Name>
            <SequencePoints><SequencePoint vc="1" sl="11" fileid="1" /></SequencePoints>
            </Method></Methods></Class></Classes></Module></Modules></CoverageSession>
            """);
        var result = await new AnalysisService([new CSharpAnalyzer()]).AnalyzeAsync(new()
            { ProjectRoot = workspace.Root, Execute = true, TimeoutSeconds = 120 }, ["code"], CancellationToken.None);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "execution_failed");
        var type = Assert.Single(Assert.Single(result.Results).Entities, entity => entity.EntityId == "csharp:Quality.Sample/Quality.Sample.csproj#Quality.Sample.Calculator");
        Assert.Equal(0, type.Metrics["method:csharp:Quality.Sample/Quality.Sample.csproj#Quality.Sample.Calculator.Choose(System.String):coverage"]);
    }
}
