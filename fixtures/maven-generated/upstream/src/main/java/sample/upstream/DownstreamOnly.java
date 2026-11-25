package sample.upstream;

public class DownstreamOnly {
    public static int measure(boolean preferred) {
        if (preferred) return 2;
        return 1;
    }
}
