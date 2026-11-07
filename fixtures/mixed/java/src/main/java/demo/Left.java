package demo;

public class Left {
    Right right;
    public int copy(int value) {
        int first = value + 1;
        int second = first * 2;
        int third = second - 3;
        return Math.abs(third);
    }
}
