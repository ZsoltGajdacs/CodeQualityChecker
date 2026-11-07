namespace Scoring;

public sealed class AtComplexity8
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
        if (value > 5)
        {
            result++;
        }
        if (value > 6)
        {
            result++;
        }
        return result;
    }
}
