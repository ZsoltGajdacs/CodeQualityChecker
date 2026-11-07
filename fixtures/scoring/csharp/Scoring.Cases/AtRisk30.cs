namespace Scoring;

public sealed class AtRisk30
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
        return result;
    }
}
