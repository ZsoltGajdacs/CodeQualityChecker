using Xunit;

namespace Scoring.Tests;

public sealed class AtSize50Tests
{
    [Fact]
    public void HasExactly50ExecutableLines()
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
        Assert.Equal(48, value);
    }
}
