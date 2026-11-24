using System.Text.Json;
using System.Text.Json.Serialization;
using Cqc.Core;
using Cqc.Java;
using Xunit;

namespace Cqc.Tests;

public sealed class JavaWorkerFactsTests
{
    [Theory]
    [InlineData(null, 4)]
    [InlineData(4, null)]
    [InlineData(-1, 4)]
    [InlineData(4, -1)]
    public void InvalidDuplicationCountsProduceAWorkerProtocolDiagnostic(int? methodLines, int? initializerLines)
    {
        var result = Read(methodLines, initializerLines);
        Assert.Equal("java.worker_protocol", Assert.Single(result.Diagnostics).Code);
        Assert.Empty(result.Methods);
        Assert.Empty(result.Executables);
    }

    [Fact]
    public void ZeroIsAValidDuplicationCount()
    {
        var result = Read(0, 0);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, Assert.Single(result.Methods).DuplicationLines);
        Assert.Equal(0, Assert.Single(result.Executables).DuplicationLines);
    }

    private static AnalysisFacts Read(int? methodLines, int? initializerLines)
    {
        var location = new Location("Sample.java", 1, 2);
        var method = new MethodFact("Sample.work", "Sample", "java", location, 1, 0, false, false, false, 0, [], [], 1)
        {
            DuplicationLines = methodLines,
            Evidence = new(true, true, true, true, true, [])
        };
        var initializer = new ExecutableFact
        {
            Id = "Sample.initializer", TypeId = "Sample", Language = "java", Location = location,
            DuplicationLines = initializerLines
        };
        var facts = new AnalysisFacts([], [method], [], [], true) { Executables = [initializer] };
        var options = new JsonSerializerOptions(Wire.Json) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        return JavaWorkerFacts.Read(JsonSerializer.Serialize(facts, options), Wire.SchemaVersion);
    }
}
