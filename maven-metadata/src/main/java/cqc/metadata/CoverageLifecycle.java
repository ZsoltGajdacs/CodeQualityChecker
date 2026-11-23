package cqc.metadata;

import org.apache.maven.AbstractMavenLifecycleParticipant;
import org.apache.maven.MavenExecutionException;
import org.apache.maven.execution.MavenSession;
import org.apache.maven.lifecycle.internal.LifecycleExecutionPlanCalculator;

import java.nio.file.Path;
import java.util.LinkedHashMap;

/** Selects per-module JaCoCo instrumentation and schedules a report after the tests. */
public final class CoverageLifecycle extends AbstractMavenLifecycleParticipant {
    private LifecycleExecutionPlanCalculator calculator;

    @Override public void afterProjectsRead(MavenSession session) throws MavenExecutionException {
        var fallbackVersion = session.getUserProperties().getProperty("cqc.jacoco.fallbackVersion");
        var scratch = session.getUserProperties().getProperty("cqc.scratchDirectory");
        if (fallbackVersion == null || scratch == null) return;
        try {
            var paths = new CoveragePaths(Path.of(scratch));
            var fallback = JacocoPlugin.plugin(fallbackVersion);
            var modules = new LinkedHashMap<ModuleCoverage, ModuleCoverage.Selection>();
            for (var project : session.getProjects()) {
                var moduleSession = session.clone();
                moduleSession.setCurrentProject(project);
                var module = new ModuleCoverage(calculator, moduleSession, paths);
                modules.put(module, module.select(fallback));
            }
            for (var entry : modules.entrySet()) entry.getKey().finish(entry.getValue());
        } catch (Exception failure) {
            throw new MavenExecutionException("Could not configure CQC Maven coverage: " + failure.getMessage(), failure);
        }
    }
}
