package cqc.metadata;

import org.apache.maven.execution.MavenSession;
import org.apache.maven.plugin.MojoExecution;
import org.apache.maven.plugin.PluginParameterExpressionEvaluator;
import org.codehaus.plexus.component.configurator.expression.ExpressionEvaluationException;

/** Effective JaCoCo settings using Maven's execution configuration and property resolution. */
record AgentSettings(String version, boolean skipped, String output, String destFile) {
    static AgentSettings resolve(MavenSession session, MojoExecution mojo) throws ExpressionEvaluationException {
        var evaluator = new PluginParameterExpressionEvaluator(session, mojo);
        return new AgentSettings(mojo.getVersion(), Boolean.parseBoolean(parameter(mojo, evaluator, "skip")),
            parameter(mojo, evaluator, "output"), parameter(mojo, evaluator, "destFile"));
    }

    String unavailableReason() {
        if (skipped) return "JaCoCo prepare-agent is explicitly skipped.";
        if (output != null && !output.equals("file")) return "The JaCoCo agent is configured without file output.";
        if (destFile == null) return "Maven did not resolve the JaCoCo execution data path.";
        return null;
    }

    private static String parameter(MojoExecution mojo, PluginParameterExpressionEvaluator evaluator, String key)
            throws ExpressionEvaluationException {
        var parameter = mojo.getConfiguration().getChild(key);
        if (parameter == null) return null;
        var value = evaluator.evaluate(parameter.getValue());
        if (value == null) value = evaluator.evaluate(parameter.getAttribute("default-value"));
        return value == null ? null : value.toString();
    }
}
