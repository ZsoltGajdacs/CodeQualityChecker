package cqc;

import java.util.*;

/** Keeps representative evidence when the same failure affects many classes in a module. */
final class CoverageDiagnostics {
    private static final int EXAMPLES = 8;
    private record Group(String code, String module) { }
    private static final class Messages {
        int count;
        final List<String> examples = new ArrayList<>();
    }

    private final Map<Group, Messages> groups = new LinkedHashMap<>();

    void add(String code, String module, String message) {
        var messages = groups.computeIfAbsent(new Group(code, module), key -> new Messages());
        messages.count++;
        if (messages.examples.size() < EXAMPLES) messages.examples.add(message);
    }

    List<CoverageMain.Diagnostic> report() {
        var diagnostics = new ArrayList<CoverageMain.Diagnostic>();
        groups.forEach((group, messages) -> append(diagnostics, group, messages));
        return diagnostics;
    }

    private static void append(List<CoverageMain.Diagnostic> diagnostics, Group group, Messages messages) {
        for (var example : messages.examples)
            diagnostics.add(new CoverageMain.Diagnostic(group.code(), example, group.module()));
        if (messages.count > EXAMPLES)
            diagnostics.add(new CoverageMain.Diagnostic(group.code(),
                (messages.count - EXAMPLES) + " additional affected classes or inputs.", group.module()));
    }
}
