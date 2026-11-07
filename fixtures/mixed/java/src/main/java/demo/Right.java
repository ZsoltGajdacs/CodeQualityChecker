package demo;

public class Right {
    Left left;
    public int copy(int input) {
        int alpha = input + 2;
        int beta = alpha * 3;
        int gamma = beta - 4;
        return Math.abs(gamma);
    }
}
