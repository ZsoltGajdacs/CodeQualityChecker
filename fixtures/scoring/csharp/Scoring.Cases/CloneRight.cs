namespace Scoring;

public sealed class CloneRight
{
    public int Measure(string value)
    {
        string trimmed = value.Trim();
        int length = trimmed.Length;
        int doubled = length * 2;
        return doubled + 1;
    }
}
