package sample.upstream;

public class Unmeasured {
    public static int measure(boolean preferred) {
        if (preferred) return 3;
        return 1;
    }
}
