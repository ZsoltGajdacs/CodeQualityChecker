package demo;

public class Calculator {
    public int choose(int value) {
        if (value > 0) return value + 1;
        return 0;
    }

    public int choose(String value) {
        return value.length();
    }

    public static class Nested {
        public int answer() { return 42; }
    }
}
