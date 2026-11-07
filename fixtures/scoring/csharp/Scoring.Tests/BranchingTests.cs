using Xunit;

namespace Scoring.Tests;

public sealed class BranchingTests
{
    [Fact]
    public void BranchesOnTrueVariable()
    {
        bool condition = true;
        int value = 0;
        if (condition)
        {
            value++;
        }
        Assert.Equal(1, value);
    }
}
