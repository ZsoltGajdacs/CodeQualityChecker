package cqc;

import java.io.*;
import java.nio.file.*;
import java.util.*;
import org.jacoco.core.analysis.Analyzer;
import org.jacoco.core.analysis.CoverageBuilder;
import org.jacoco.core.data.ExecutionData;
import org.jacoco.core.data.ExecutionDataStore;
import org.jacoco.report.xml.XMLFormatter;

/** Produces counters only for classes with attributable downstream probes. */
final class CoverageOverlay {
    private final CoverageMain.Owner owner;
    private final ExecutionDataStore local;
    private final CoverageDiagnostics diagnostics;

    CoverageOverlay(CoverageMain.Owner owner, ExecutionDataStore local, CoverageDiagnostics diagnostics) {
        this.owner = owner;
        this.local = local;
        this.diagnostics = diagnostics;
    }

    boolean write(Map<CoverageOwnership.ProductionClass, List<ReactorCoverage.Observation>> classes) {
        if (classes.values().stream().flatMap(Collection::stream).noneMatch(this::downstream)) return false;
        if (owner.localReport() && local == null) {
            diagnostics.add("java.coverage_local_unavailable", owner.module(),
                "Local JaCoCo XML exists but its execution data is unavailable for merging downstream probes.");
            return false;
        }
        var coverage = new CoverageBuilder();
        classes.forEach((target, inputs) -> analyze(coverage, target.file(), inputs));
        if (coverage.getClasses().isEmpty()) return false;
        try {
            writeReport(coverage);
            return true;
        } catch (IOException | RuntimeException exception) {
            diagnostics.add("java.coverage_overlay_failed", owner.module(),
                "Cannot write merged JaCoCo coverage: " + exception.getMessage());
            return false;
        }
    }

    private void analyze(CoverageBuilder coverage, CoverageClassFiles.ClassFile file, List<ReactorCoverage.Observation> inputs) {
        if (inputs.stream().noneMatch(this::downstream)) return;
        var name = inputs.get(0).data().getName();
        if (missingLocalProbes(name, inputs)) return;
        try {
            var bytes = file.checkedBytes();
            var merged = mergeClass(new ExecutionData(file.id(), name, ProbeLayout.count(bytes)), inputs);
            if (merged.isEmpty()) return;
            var data = new ExecutionDataStore();
            data.put(merged.get());
            new Analyzer(data, coverage).analyzeClass(bytes, name);
        } catch (IOException | RuntimeException exception) {
            diagnostics.add("java.coverage_class_unreadable", owner.module(),
                "Cannot analyze compiled coverage class " + name + ": " + exception.getMessage());
        }
    }

    private boolean missingLocalProbes(String name, List<ReactorCoverage.Observation> inputs) {
        if (!owner.localReport() || !local.contains(name)) return false;
        if (inputs.stream().anyMatch(input -> !downstream(input))) return false;
        diagnostics.add("java.coverage_local_unavailable", owner.module(),
            "Could not attribute the local JaCoCo probes for " + name + "; retaining its local XML.");
        return true;
    }

    private Optional<ExecutionData> mergeClass(ExecutionData merged, List<ReactorCoverage.Observation> inputs) {
        boolean hasDownstream = false;
        boolean invalidLocal = false;
        for (var input : inputs) {
            if (merge(merged, input)) hasDownstream |= downstream(input);
            else invalidLocal |= !downstream(input);
        }
        return hasDownstream && !(owner.localReport() && invalidLocal) ? Optional.of(merged) : Optional.empty();
    }

    private boolean merge(ExecutionData merged, ReactorCoverage.Observation input) {
        try {
            merged.merge(input.data());
            return true;
        } catch (IllegalStateException exception) {
            diagnostics.add("java.coverage_probe_mismatch", input.module(),
                "JaCoCo probes for " + merged.getName() + " are incompatible with " + owner.module() + ": " + exception.getMessage());
            return false;
        }
    }

    private boolean downstream(ReactorCoverage.Observation input) {
        return !owner.module().equals(input.module());
    }

    private void writeReport(CoverageBuilder coverage) throws IOException {
        var xml = new ByteArrayOutputStream();
        var report = new XMLFormatter().createVisitor(xml);
        report.visitInfo(List.of(), List.of());
        report.visitBundle(coverage.getBundle(owner.module()), null);
        report.visitEnd();
        Files.write(Path.of(owner.report()), xml.toByteArray(), StandardOpenOption.CREATE_NEW, StandardOpenOption.WRITE);
    }
}
