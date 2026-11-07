namespace Quality.Sample;

public class Left
{
    public Right? Right { get; set; }
    public int Copy(int value)
    {
        int first = value + 1;
        int second = first * 2;
        int third = second - 3;
        return Math.Abs(third);
    }
}
