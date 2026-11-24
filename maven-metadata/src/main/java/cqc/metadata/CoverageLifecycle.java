package cqc.metadata;

import org.apache.maven.AbstractMavenLifecycleParticipant;
import org.apache.maven.MavenExecutionException;
import org.apache.maven.execution.MavenSession;
import org.apache.maven.lifecycle.internal.LifecycleExecutionPlanCalculator;

import java.nio.file.Path;
import java.util.LinkedHashMap;
import java.util.Objects;

/** Selects per-module JaCoCo instrumentation and schedules a report after the tests. */
public final class CoverageLifecycle extends AbstractMavenLifecycleParticipant {
    private LifecycleExecutionPlanCalculator calculator;

    @Override public void afterProjectsRead(MavenSession session) throws MavenExecutionException {
        var properties = session.getUserProperties();
        var reactorDirectory = properties.getProperty("cqc.reactorDirectory");
        if (reactorDirectory == null) return;
        try {
            var fallbackVersion = Objects.requireNonNull(properties.getProperty("cqc.jacoco.fallbackVersion"), "Missing fallback JaCoCo version.");
            var projectRoot = Objects.requireNonNull(properties.getProperty("cqc.projectRoot"), "Missing project root.");
            var paths = new CoveragePaths(Path.of(projectRoot));
            var output = Path.of(reactorDirectory).toRealPath();
            var fallback = JacocoPlugin.plugin(fallbackVersion);
            var modules = new LinkedHashMap<ModuleCoverage, ModuleCoverage.Selection>();
            for (var project : session.getProjects()) {
                var moduleSession = session.clone();
                moduleSession.setCurrentProject(project);
                var module = new ModuleCoverage(calculator, moduleSession, paths, ModuleOutput.create(output, project));
                modules.put(module, module.select(fallback));
            }
            for (var entry : modules.entrySet()) entry.getKey().finish(entry.getValue());
        } catch (Exception failure) {
            throw new MavenExecutionException("Could not configure CQC Maven coverage: " + failure.getMessage(), failure);
        }
    }
}
