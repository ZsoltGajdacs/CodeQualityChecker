namespace Quality.Sample;

public partial class Calculator
{
    public int Choose(int value)
    {
        if (value > 0) return value + 1;
        return 0;
    }

    public int Choose(string value) => value.Length;
    public int Answer => 42;
    public class Nested { public int Read() => 42; }
}
