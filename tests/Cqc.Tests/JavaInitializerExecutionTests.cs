using Cqc.Core;
using Cqc.Java;
using Xunit;

namespace Cqc.Tests;

public sealed class JavaInitializerExecutionTests
{
    [Fact]
    [Trait("Category", "Execution")]
    public async Task CompactConstructorUsesJaCoCoWhileFieldInitializationHasStaticMetrics()
    {
        using var workspace = FixtureWorkspace.Copy("mixed");
        var root = Path.Combine(workspace.Root, "java");
        await Write(root, "src/main/java/demo/Measured.java", """
            package demo;
            public record Measured(int number) {
                static final int SEED = Math.abs(-2);
                public Measured {
                    if (number < 0) number = -number;
                }
            }
            """);
        await Write(root, "src/test/java/demo/MeasuredTest.java", """
            package demo;
            import org.junit.jupiter.api.Test;
            import static org.junit.jupiter.api.Assertions.assertEquals;
            class MeasuredTest {
                @Test void normalizesBothPaths() {
                    assertEquals(2, new Measured(-2).number());
                    assertEquals(3, new Measured(3).number());
                }
            }
            """);
        var result = await new AnalysisService([new JavaAnalyzer()]).AnalyzeAsync(
            new() { ProjectRoot = root, Execute = true }, ["code"], CancellationToken.None);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "execution_failed");
        var entity = Assert.Single(Assert.Single(result.Results).Entities, score =>
            score.Kind == "class" && score.EntityId.EndsWith("::demo.Measured", StringComparison.Ordinal));
        Assert.Equal(10, entity.Score);
        Assert.Equal(10, entity.StaticComplexity!.Score);
        Assert.Contains(entity.Metrics, metric => metric.Key.EndsWith("Measured..ctor(int):coverage", StringComparison.Ordinal)
            && metric.Value == 1);
        var initializer = Assert.Single(entity.Metrics, metric => metric.Key.StartsWith("executable:", StringComparison.Ordinal));
        Assert.EndsWith(":complexity", initializer.Key);
        Assert.Equal(1, initializer.Value);
    }

    private static Task Write(string root, string file, string text) => File.WriteAllTextAsync(Path.Combine(root, file), text);
}
