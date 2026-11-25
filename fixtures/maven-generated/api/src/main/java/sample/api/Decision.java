package sample.api;

public class Decision {
    public static int value(boolean enabled) {
        if (enabled) return 1;
        return 0;
    }
}
