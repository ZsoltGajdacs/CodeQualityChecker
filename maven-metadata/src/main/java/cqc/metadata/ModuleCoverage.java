package cqc.metadata;

import org.apache.maven.execution.MavenSession;
import org.apache.maven.lifecycle.internal.LifecycleExecutionPlanCalculator;
import org.apache.maven.lifecycle.internal.LifecycleTask;
import org.apache.maven.model.Plugin;
import org.apache.maven.model.PluginExecution;
import org.apache.maven.plugin.MojoExecution;
import org.apache.maven.project.MavenProject;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;

import static cqc.metadata.JacocoPlugin.Execution.FALLBACK;
import static cqc.metadata.JacocoPlugin.Execution.REPORT;

/** Configures JaCoCo in one module's evaluated Maven test plan. */
final class ModuleCoverage {
    private final LifecycleExecutionPlanCalculator calculator;
    private final MavenSession session;
    private final MavenProject project;
    private final CoveragePaths paths;
    private Plugin addedPlugin;
    private PluginExecution fallback;

    ModuleCoverage(LifecycleExecutionPlanCalculator calculator, MavenSession session, CoveragePaths paths) {
        this.calculator = calculator;
        this.session = session;
        this.project = session.getCurrentProject();
        this.paths = paths;
    }

    Selection select(Plugin fallbackPlugin) throws Exception {
        var plan = calculate();
        int test = firstSurefireTest(plan);
        var agents = plan.stream().filter(JacocoPlugin::preparesAgent).toList();
        if (agents.isEmpty() && test >= 0) return selectFallback(fallbackPlugin);

        var resolved = new ArrayList<ResolvedAgent>();
        for (var agent : agents) resolved.add(resolve(agent));
        if (test < 0) return Selection.unavailable(null, "Maven scheduled no Surefire tests for the module.");
        if (resolved.size() > 1)
            return Selection.unavailable(null, "Maven scheduled multiple JaCoCo agent executions.");

        var agent = resolved.get(0);
        if (plan.indexOf(agents.get(0)) > test)
            return Selection.unavailable(agent.version(), "The project JaCoCo agent is scheduled after Surefire starts.");
        return selectReport(agent);
    }

    private Selection selectFallback(Plugin fallbackPlugin) throws Exception {
        var plugin = project.getPlugin(JacocoPlugin.KEY);
        if (plugin != null && FALLBACK.idInUse(plugin))
            return Selection.unavailable(null, "A project JaCoCo execution already uses the CQC fallback execution ID.");
        if (plugin == null) {
            plugin = fallbackPlugin.clone();
            project.getBuild().addPlugin(plugin);
            project.getBuild().flushPluginMap();
            addedPlugin = plugin;
        }
        fallback = FALLBACK.create();
        JacocoPlugin.add(plugin, fallback);
        var agent = calculate().stream().filter(FALLBACK::scheduled).findFirst().orElseThrow();
        return selectReport(resolve(agent));
    }

    private ResolvedAgent resolve(MojoExecution mojo) throws Exception {
        calculator.setupMojoExecution(session, project, mojo);
        var settings = AgentSettings.resolve(session, mojo);
        var reason = settings.unavailableReason();
        if (reason != null) return new ResolvedAgent(settings.version(), null, reason);

        var configured = project.getBasedir().toPath().resolve(settings.destFile());
        var dataFile = paths.insideScratch(configured);
        if (dataFile == null)
            return new ResolvedAgent(settings.version(), null, "The JaCoCo execution data path is outside the isolated copy.");
        paths.claim(dataFile);
        if (Files.exists(dataFile))
            return new ResolvedAgent(settings.version(), null, "The JaCoCo execution data path already exists.");
        return new ResolvedAgent(settings.version(), dataFile, null);
    }

    private Selection selectReport(ResolvedAgent agent) {
        if (agent.reason() != null) return Selection.unavailable(agent.version(), agent.reason());

        var plugin = project.getPlugin(JacocoPlugin.KEY);
        if (REPORT.idInUse(plugin))
            return Selection.unavailable(agent.version(), "A project JaCoCo execution already uses the CQC report execution ID.");
        var report = paths.insideScratch(Path.of(project.getBuild().getDirectory(), "cqc-coverage", "jacoco.xml"));
        if (report == null || Files.exists(report))
            return Selection.unavailable(agent.version(), "The CQC JaCoCo report path is outside the isolated copy or already exists.");
        paths.claim(report);
        return new Selection(agent.version(), agent.dataFile(), report, null);
    }

    void finish(Selection selection) throws Exception {
        var result = scheduleReport(selection);
        if (result.report() == null) removeFallback();
        result.attachTo(project);
    }

    private CoverageResult scheduleReport(Selection selection) throws Exception {
        if (selection.reason() != null) return new CoverageResult(selection.version(), null, selection.reason());
        if (paths.shared(selection.dataFile()) || paths.shared(selection.report()))
            return new CoverageResult(selection.version(), null, "Multiple Maven modules claim the JaCoCo execution data or the CQC report path.");

        var plugin = project.getPlugin(JacocoPlugin.KEY);
        var report = JacocoPlugin.report(selection.dataFile(), selection.report().getParent());
        JacocoPlugin.add(plugin, report);
        if (reportFollowsSurefire(calculate())) return new CoverageResult(selection.version(), selection.report(), null);

        JacocoPlugin.remove(plugin, report);
        return new CoverageResult(selection.version(), null, "Maven did not schedule the CQC JaCoCo report after all Surefire executions.");
    }

    private void removeFallback() {
        if (fallback == null) return;
        JacocoPlugin.remove(project.getPlugin(JacocoPlugin.KEY), fallback);
        if (addedPlugin != null) {
            project.getBuild().removePlugin(addedPlugin);
            project.getBuild().flushPluginMap();
        }
    }

    private static boolean isSurefireTest(MojoExecution mojo) {
        return "org.apache.maven.plugins".equals(mojo.getGroupId()) &&
            "maven-surefire-plugin".equals(mojo.getArtifactId()) && "test".equals(mojo.getGoal());
    }

    private static int firstSurefireTest(List<MojoExecution> plan) {
        for (int index = 0; index < plan.size(); index++) if (isSurefireTest(plan.get(index))) return index;
        return -1;
    }

    private static boolean reportFollowsSurefire(List<MojoExecution> plan) {
        boolean reportSeen = false;
        for (var mojo : plan) {
            if (isSurefireTest(mojo) && reportSeen) return false;
            if (REPORT.scheduled(mojo)) reportSeen = true;
        }
        return reportSeen;
    }

    private List<MojoExecution> calculate() throws Exception {
        return calculator.calculateExecutionPlan(session, project, List.of(new LifecycleTask("test")), false)
            .getMojoExecutions();
    }
    record Selection(String version, Path dataFile, Path report, String reason) {
        static Selection unavailable(String version, String reason) {
            return new Selection(version, null, null, reason);
        }
    }

    private record ResolvedAgent(String version, Path dataFile, String reason) {}
}
