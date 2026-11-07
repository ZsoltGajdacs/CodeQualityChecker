namespace Scoring;

public sealed class OverRisk42
{
    public int Evaluate(int value)
    {
        int result = 0;
        if (value > 0)
        {
            result++;
        }
        if (value > 1)
        {
            result++;
        }
        if (value > 2)
        {
            result++;
        }
        if (value > 3)
        {
            result++;
        }
        if (value > 4)
        {
            result++;
        }
        return result;
    }
}
