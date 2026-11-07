using Xunit;

namespace Scoring.Tests;

public sealed class CoverageTests
{
    [Fact]
    public void HealthyHasBothInputs()
    {
        Assert.Equal(0, new Healthy().Evaluate(-1));
        Assert.Equal(101, new Healthy().Evaluate(100));
    }

    [Fact]
    public void AtComplexityBoundaryHasBothInputs()
    {
        Assert.Equal(0, new AtComplexity8().Evaluate(-1));
        Assert.Equal(7, new AtComplexity8().Evaluate(100));
    }

    [Fact]
    public void OverComplexityBoundaryHasBothInputs()
    {
        Assert.Equal(0, new OverComplexity9().Evaluate(-1));
        Assert.Equal(8, new OverComplexity9().Evaluate(100));
    }

    [Fact]
    public void Complexity10HasBothInputs()
    {
        Assert.Equal(0, new AtComplexity10().Evaluate(-1));
        Assert.Equal(9, new AtComplexity10().Evaluate(100));
    }

    [Fact]
    public void Complexity11HasBothInputs()
    {
        Assert.Equal(0, new OverComplexity11().Evaluate(-1));
        Assert.Equal(10, new OverComplexity11().Evaluate(100));
    }
}
