package cqc.metadata;

import org.apache.maven.execution.MavenSession;
import org.apache.maven.lifecycle.internal.LifecycleExecutionPlanCalculator;
import org.apache.maven.lifecycle.internal.LifecycleTask;
import org.apache.maven.model.Plugin;
import org.apache.maven.model.PluginExecution;
import org.apache.maven.plugin.MojoExecution;
import org.apache.maven.project.MavenProject;

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
    private final ModuleOutput output;
    private Plugin addedPlugin;
    private PluginExecution fallback;

    ModuleCoverage(LifecycleExecutionPlanCalculator calculator, MavenSession session, CoveragePaths paths, ModuleOutput output) {
        this.calculator = calculator;
        this.session = session;
        this.project = session.getCurrentProject();
        this.paths = paths;
        this.output = output;
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
        return Selection.from(agent);
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
        return Selection.from(resolve(agent));
    }

    private ResolvedAgent resolve(MojoExecution mojo) throws Exception {
        calculator.setupMojoExecution(session, project, mojo);
        var settings = AgentSettings.resolve(session, mojo);
        var reason = settings.unavailableReason();
        if (reason != null) return new ResolvedAgent(mojo, null, reason);

        var configured = project.getBasedir().toPath().resolve(settings.destFile());
        var dataFile = paths.insideProject(configured);
        if (dataFile == null)
            return new ResolvedAgent(mojo, null, "The configured JaCoCo execution data path is outside the project.");
        paths.claim(dataFile);
        return new ResolvedAgent(mojo, dataFile, null);
    }

    void finish(Selection selection) throws Exception {
        var result = scheduleReport(selection);
        if (result.report() == null) removeFallback();
        result.attachTo(project);
    }

    private CoverageResult scheduleReport(Selection selection) throws Exception {
        if (selection.reason() != null) return new CoverageResult(selection.version(), null, selection.reason());
        var selected = selection.agent();
        if (paths.shared(selected.configuredData()))
            return new CoverageResult(selection.version(), null, "Multiple Maven modules claim the configured JaCoCo execution data path.");

        var plugin = project.getPlugin(JacocoPlugin.KEY);
        if (REPORT.idInUse(plugin))
            return new CoverageResult(selection.version(), null, "A project JaCoCo execution already uses the CQC report execution ID.");
        var agent = plugin.getExecutionsAsMap().get(selected.mojo().getExecutionId());
        if (agent == null)
            return new CoverageResult(selection.version(), null, "Maven did not expose the selected JaCoCo agent configuration.");

        var original = agent.getConfiguration();
        JacocoPlugin.redirectAgent(agent, output.data());
        var report = JacocoPlugin.report(output.data(), output.directory());
        JacocoPlugin.add(plugin, report);
        var plan = calculate();
        var failure = verifyCoveragePlan(plan, selected);
        if (failure == null) return new CoverageResult(selection.version(), output.report(), null);

        JacocoPlugin.remove(plugin, report);
        agent.setConfiguration(original);
        return new CoverageResult(selection.version(), null, failure);
    }

    private String verifyCoveragePlan(List<MojoExecution> plan, ResolvedAgent selected) throws Exception {
        if (!reportFollowsSurefire(plan))
            return "Maven did not schedule the CQC JaCoCo report after all Surefire executions.";
        for (var mojo : plan) {
            if (!selected.matches(mojo)) continue;
            calculator.setupMojoExecution(session, project, mojo);
            if (output.data().toString().equals(AgentSettings.resolve(session, mojo).destFile())) return null;
        }
        return "Maven did not route the selected JaCoCo agent to this run's execution data.";
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
    record Selection(String version, ResolvedAgent agent, String reason) {
        static Selection unavailable(String version, String reason) {
            return new Selection(version, null, reason);
        }

        static Selection from(ResolvedAgent agent) {
            return new Selection(agent.version(), agent, agent.reason());
        }
    }

    private record ResolvedAgent(MojoExecution mojo, Path configuredData, String reason) {
        String version() { return mojo.getVersion(); }

        boolean matches(MojoExecution candidate) {
            return JacocoPlugin.preparesAgent(candidate) && mojo.getExecutionId().equals(candidate.getExecutionId())
                && mojo.getGoal().equals(candidate.getGoal());
        }
    }
}
