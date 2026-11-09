package cqc;

/** Diagnostic boundary shared by read-only Maven metadata readers and language analysis. */
final class MavenClasspath {
    interface Reporter { void report(String code, String message, String file); }
    private MavenClasspath() { }
}
