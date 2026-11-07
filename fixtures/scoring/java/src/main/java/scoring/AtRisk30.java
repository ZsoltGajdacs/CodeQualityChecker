package scoring;

public class AtRisk30 {
    public int evaluate(int value) {
        int result = 0;
        if (value > 0) {
            result++;
        }
        if (value > 1) {
            result++;
        }
        if (value > 2) {
            result++;
        }
        if (value > 3) {
            result++;
        }
        return result;
    }
}
