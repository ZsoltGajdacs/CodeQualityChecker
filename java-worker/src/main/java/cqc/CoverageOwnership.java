package cqc;

import java.util.*;
import org.jacoco.core.data.ExecutionData;

/** Maps execution data to a uniquely matching production owner visible to the test JVM. */
final class CoverageOwnership {
    record ProductionClass(CoverageMain.Owner owner, CoverageClassFiles.ClassFile file) { }
    private final CoverageClassFiles files = new CoverageClassFiles();
    private final CoverageClasspath classpath;
    private final CoverageDiagnostics diagnostics;

    CoverageOwnership(CoverageDiagnostics diagnostics) {
        this.classpath = new CoverageClasspath(files, diagnostics);
        this.diagnostics = diagnostics;
    }

    Optional<ProductionClass> find(ExecutionData data, CoverageMain.Input input, List<CoverageMain.Owner> visible) {
        try {
            return match(data, input, visible);
        } catch (RuntimeException exception) {
            diagnostics.add("java.coverage_class_unreadable", input.module(),
                "Cannot read compiled coverage class " + data.getName() + ": " + exception.getMessage());
            return Optional.empty();
        }
    }

    private Optional<ProductionClass> match(ExecutionData data, CoverageMain.Input input, List<CoverageMain.Owner> visible) {
        var candidates = candidates(data.getName(), visible);
        if (candidates.isEmpty()) return Optional.empty();
        var matches = candidates.stream().filter(candidate -> candidate.file().id() == data.getId()).toList();
        if (matches.isEmpty()) return unmatched(data, input, candidates);
        var proven = matches.stream().filter(candidate -> classpath.proves(input, data, candidate.owner().module())).toList();
        if (proven.isEmpty()) {
            diagnostics.add("java.coverage_class_mismatch", input.module(),
                "Execution data for " + data.getName() + " does not match an evaluated production path for " + modules(matches) + ".");
            return Optional.empty();
        }
        if (testClass(data, input)) {
            reportAmbiguity(data, input, modules(proven) + " and this module's test output");
            return Optional.empty();
        }
        if (proven.size() == 1) return uniqueMatch(input, data, proven.get(0));
        reportAmbiguity(data, input, modules(proven));
        return Optional.empty();
    }

    private Optional<ProductionClass> unmatched(ExecutionData data, CoverageMain.Input input, List<ProductionClass> candidates) {
        if (!testClass(data, input))
            diagnostics.add("java.coverage_class_mismatch", input.module(),
                "Execution data for " + data.getName() + " does not match the compiled classes in " + modules(candidates) + ".");
        return Optional.empty();
    }

    private Optional<ProductionClass> uniqueMatch(CoverageMain.Input input, ExecutionData data, ProductionClass owner) {
        return classpath.attributable(input, data, owner.owner().module()) ? Optional.of(owner) : Optional.empty();
    }

    private boolean testClass(ExecutionData data, CoverageMain.Input input) {
        return files.find(input.testClasses(), data.getName())
            .map(file -> file.id() == data.getId()).orElse(false);
    }

    private void reportAmbiguity(ExecutionData data, CoverageMain.Input input, String matches) {
        diagnostics.add("java.coverage_owner_ambiguous", input.module(),
            "Execution data for " + data.getName() + " matches " + matches + ".");
    }

    private List<ProductionClass> candidates(String name, List<CoverageMain.Owner> visible) {
        var result = new ArrayList<ProductionClass>();
        for (var owner : visible)
            files.find(owner.classes(), name).ifPresent(file -> result.add(new ProductionClass(owner, file)));
        return result;
    }

    private static String modules(List<ProductionClass> candidates) {
        return String.join(", ", candidates.stream().map(candidate -> candidate.owner().module()).toList());
    }
}
