using Xunit;

namespace Quality.Sample.Tests;

public class CalculatorTests
{
    private readonly Calculator calculator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ChoosesPositive(int value) => Check(value);

    private void Check(int value) => Assert.Equal(value + 1, calculator.Choose(value));

    [Fact]
    public void WeakExample() { calculator.Choose(0); }
}
