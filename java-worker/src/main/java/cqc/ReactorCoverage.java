package cqc;

import java.io.IOException;
import java.nio.file.*;
import java.util.*;
import org.jacoco.core.data.ExecutionData;
import org.jacoco.core.data.ExecutionDataStore;
import org.jacoco.core.tools.ExecFileLoader;

/** Joins evaluated Maven test classpaths with their execution data before rendering any report. */
final class ReactorCoverage {
    private static final Set<String> SUPPORTED_AGENTS = Set.of("0.8.14", "0.8.15");
    record Observation(String module, ExecutionData data) { }
    private final Map<String, CoverageMain.Owner> owners = new LinkedHashMap<>();
    private final Map<String, ExecutionDataStore> localRuns = new HashMap<>();
    private final Map<CoverageMain.Owner, Map<CoverageOwnership.ProductionClass, List<Observation>>> observations = new HashMap<>();
    private final CoverageDiagnostics diagnostics = new CoverageDiagnostics();
    private final CoverageOwnership ownership = new CoverageOwnership(diagnostics);

    ReactorCoverage(List<CoverageMain.Owner> owners) {
        for (var owner : owners) this.owners.put(owner.module(), owner);
    }

    CoverageMain.Response merge(List<CoverageMain.Input> inputs) {
        for (var input : inputs) ingest(input);
        var reports = new ArrayList<String>();
        for (var owner : owners.values())
            if (overlay(owner)) reports.add(owner.module());
        return new CoverageMain.Response(reports, diagnostics.report());
    }

    private boolean overlay(CoverageMain.Owner owner) {
        var classes = observations.getOrDefault(owner, Map.of());
        var local = localRuns.get(owner.module());
        var overlay = new CoverageOverlay(owner, local, diagnostics);
        return overlay.write(classes);
    }

    private void ingest(CoverageMain.Input input) {
        var data = load(input);
        if (data.isEmpty()) return;

        var visible = new LinkedHashSet<>(input.visibleModules());
        visible.add(input.module());
        var visibleOwners = visible.stream().map(owners::get).filter(Objects::nonNull).toList();
        localRuns.put(input.module(), data.get());
        for (var entry : data.get().getContents())
            ownership.find(entry, input, visibleOwners).ifPresent(target -> record(target, new Observation(input.module(), entry)));
    }

    private void record(CoverageOwnership.ProductionClass target, Observation observation) {
        observations.computeIfAbsent(target.owner(), owner -> new LinkedHashMap<>())
            .computeIfAbsent(target, key -> new ArrayList<>()).add(observation);
    }

    private Optional<ExecutionDataStore> load(CoverageMain.Input input) {
        if (!SUPPORTED_AGENTS.contains(Objects.toString(input.agentVersion(), ""))) {
            diagnostics.add("java.coverage_agent_unsupported", input.module(),
                "Cannot merge execution data from JaCoCo agent version " + Objects.toString(input.agentVersion(), "unknown") + ".");
            return Optional.empty();
        }
        try {
            var path = Path.of(input.data());
            if (!Files.isRegularFile(path, LinkOption.NOFOLLOW_LINKS) || Files.size(path) == 0)
                throw new IOException("Expected a nonempty staged JaCoCo execution data file.");
            var loader = new ExecFileLoader();
            loader.load(path.toFile());
            return Optional.of(loader.getExecutionDataStore());
        } catch (IOException | RuntimeException exception) {
            diagnostics.add("java.coverage_exec_invalid", input.module(),
                "Cannot read staged JaCoCo execution data: " + exception.getMessage());
            return Optional.empty();
        }
    }
}
