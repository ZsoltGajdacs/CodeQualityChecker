package cqc;

/** Diagnostic boundary shared by read-only Maven metadata readers and language analysis. */
final class MavenClasspath {
    interface Reporter {
        void report(Worker.Diagnostic diagnostic);

        default void report(String code, String message, String file) {
            report(new Worker.Diagnostic(code, message, file, "java", null, null));
        }

        default void report(String code, String message, Worker.Location location, String construct) {
            report(new Worker.Diagnostic(code, message, location.file(), "java", location, construct));
        }
    }
    private MavenClasspath() { }
}
