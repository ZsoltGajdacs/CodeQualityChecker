package scoring;

public class CloneRight {
    public int measure(String value) {
        String trimmed = value.trim();
        int length = trimmed.length();
        int doubled = length * 2;
        return doubled + 1;
    }
}
