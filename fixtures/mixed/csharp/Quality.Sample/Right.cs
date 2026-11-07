namespace Quality.Sample;

public class Right
{
    public Left? Left { get; set; }
    public int Copy(int input)
    {
        int alpha = input + 2;
        int beta = alpha * 3;
        int gamma = beta - 4;
        return Math.Abs(gamma);
    }
}
