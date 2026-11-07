using Xunit;

namespace Scoring.Tests;

public sealed class OverSize51Tests
{
    [Fact]
    public void HasExactly51ExecutableLines()
    {
        int value = 0;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        value++;
        Assert.Equal(49, value);
    }
}
