package cqc;

import java.io.*;
import java.nio.file.*;
import java.util.*;
import org.jacoco.core.data.ExecutionData;

/** Rejects ownership if another evaluated classpath entry could supply the same loaded class. */
final class CoverageClasspath {
    private interface Artifact { boolean matches(ExecutionData data, boolean includeInactiveVariants) throws IOException; }
    private final Map<String, Artifact> artifacts = new HashMap<>();
    private final CoverageClassFiles files;
    private final CoverageDiagnostics diagnostics;

    CoverageClasspath(CoverageClassFiles files, CoverageDiagnostics diagnostics) {
        this.files = files;
        this.diagnostics = diagnostics;
    }

    boolean proves(CoverageMain.Input input, ExecutionData data, String owner) {
        if (input.productionPaths() == null) return true;
        var paths = productionPaths(input, owner);
        for (var path : paths)
            if (matches(input, data, path, false).orElse(false)) return true;
        return false;
    }

    boolean attributable(CoverageMain.Input input, ExecutionData data, String owner) {
        var owned = productionPaths(input, owner);
        for (var path : Objects.requireNonNullElse(input.competingPaths(), List.<String>of()))
            if (!owned.contains(path) && !distinct(input, data, path)) return false;
        return true;
    }

    private static List<String> productionPaths(CoverageMain.Input input, String owner) {
        return input.productionPaths() == null ? List.of() : input.productionPaths().getOrDefault(owner, List.of());
    }

    private boolean distinct(CoverageMain.Input input, ExecutionData data, String path) {
        var match = matches(input, data, path, true);
        if (match.isEmpty()) return false;
        if (match.get())
            diagnostics.add("java.coverage_owner_ambiguous", input.module(),
                "Execution data for " + data.getName() + " also matches " + path + ".");
        return !match.get();
    }

    private Optional<Boolean> matches(CoverageMain.Input input, ExecutionData data, String path, boolean includeInactiveVariants) {
        try {
            return Optional.of(artifacts.computeIfAbsent(path, this::artifact).matches(data, includeInactiveVariants));
        } catch (IOException | RuntimeException exception) {
            diagnostics.add("java.coverage_classpath_unreadable", input.module(),
                "Cannot verify classpath ownership of " + data.getName() + " against " + path + ": " + exception.getMessage());
            return Optional.empty();
        }
    }

    private Artifact artifact(String value) {
        var path = Path.of(value);
        try {
            if (Files.isDirectory(path))
                return (data, variants) -> files.find(value, data.getName()).map(file -> file.id() == data.getId()).orElse(false);
            var jar = new CoverageJar(path);
            return jar::matches;
        } catch (IOException | RuntimeException exception) {
            return (data, variants) -> { throw new IOException(exception.getMessage(), exception); };
        }
    }

}
