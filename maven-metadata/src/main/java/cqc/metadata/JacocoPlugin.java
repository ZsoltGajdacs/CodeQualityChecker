package cqc.metadata;

import org.apache.maven.model.Plugin;
import org.apache.maven.model.PluginExecution;
import org.apache.maven.plugin.MojoExecution;
import org.codehaus.plexus.util.xml.Xpp3Dom;

import java.nio.file.Path;

/** Maven identifiers and in-memory goal configuration for JaCoCo. */
final class JacocoPlugin {
    static final String KEY = "org.jacoco:jacoco-maven-plugin";
    private static final String PREPARE = "prepare-agent";

    private JacocoPlugin() {}

    static Plugin plugin(String version) {
        var plugin = new Plugin();
        plugin.setGroupId("org.jacoco");
        plugin.setArtifactId("jacoco-maven-plugin");
        plugin.setVersion(version);
        return plugin;
    }

    static PluginExecution report(Path dataFile, Path directory) {
        var report = Execution.REPORT.create();
        report.setPriority(Integer.MAX_VALUE);
        var config = new Xpp3Dom("configuration");
        child(config, "dataFile", dataFile.toString());
        child(config, "outputDirectory", directory.toString());
        var formats = child(config, "formats", null);
        formats.setAttribute("combine.self", "override");
        child(formats, "format", "XML");
        report.setConfiguration(config);
        return report;
    }

    static void redirectAgent(PluginExecution agent, Path dataFile) {
        var original = (Xpp3Dom) agent.getConfiguration();
        var config = original == null ? new Xpp3Dom("configuration") : new Xpp3Dom(original);
        var destination = config.getChild("destFile");
        if (destination == null) child(config, "destFile", dataFile.toString());
        else destination.setValue(dataFile.toString());
        agent.setConfiguration(config);
    }

    static boolean preparesAgent(MojoExecution mojo) {
        return isJacoco(mojo) && (PREPARE.equals(mojo.getGoal()) || "prepare-agent-integration".equals(mojo.getGoal()));
    }

    static void add(Plugin plugin, PluginExecution execution) {
        plugin.addExecution(execution);
        plugin.flushExecutionMap();
    }

    static void remove(Plugin plugin, PluginExecution execution) {
        plugin.removeExecution(execution);
        plugin.flushExecutionMap();
    }

    private static boolean isJacoco(MojoExecution mojo) {
        return KEY.equals(mojo.getGroupId() + ":" + mojo.getArtifactId());
    }

    private static Xpp3Dom child(Xpp3Dom parent, String key, String value) {
        var child = new Xpp3Dom(key);
        child.setValue(value);
        parent.addChild(child);
        return child;
    }

    enum Execution {
        FALLBACK("cqc-prepare-agent", "initialize", PREPARE),
        REPORT("cqc-report", "test", "report");

        private final String id;
        private final String phase;
        private final String goal;

        Execution(String id, String phase, String goal) {
            this.id = id;
            this.phase = phase;
            this.goal = goal;
        }

        PluginExecution create() {
            var execution = new PluginExecution();
            execution.setId(id);
            execution.setPhase(phase);
            execution.addGoal(goal);
            return execution;
        }

        boolean scheduled(MojoExecution mojo) {
            return isJacoco(mojo) && id.equals(mojo.getExecutionId()) && goal.equals(mojo.getGoal());
        }

        boolean idInUse(Plugin plugin) {
            return plugin.getExecutionsAsMap().containsKey(id);
        }
    }
}
