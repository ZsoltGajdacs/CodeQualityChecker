package cqc.metadata;

import org.apache.maven.eventspy.AbstractEventSpy;
import org.apache.maven.execution.ExecutionEvent;
import org.apache.maven.project.MavenProject;
import org.apache.maven.artifact.Artifact;
import java.nio.file.*;
import java.util.List;
import javax.xml.stream.*;

/** Captures Maven's evaluated project model at the final dependency-resolution goal of each module. */
public final class MetadataSpy extends AbstractEventSpy {
    @Override public void onEvent(Object event) throws Exception {
        if (!(event instanceof ExecutionEvent execution) || execution.getType() != ExecutionEvent.Type.MojoSucceeded) return;
        var mojo = execution.getMojoExecution();
        if (!"default-cli".equals(mojo.getExecutionId())) return;
        if (!mojo.getArtifactId().equals("maven-dependency-plugin") || !mojo.getGoal().equals("build-classpath")) return;
        var project = execution.getProject();
        var output = ModuleOutput.from(project);
        if (output != null) writeProject(project, output.metadata());
    }

    private static void writeProject(MavenProject project, Path output) throws Exception {
        try (var stream = Files.newOutputStream(output, StandardOpenOption.CREATE_NEW, StandardOpenOption.WRITE)) {
            var xml = XMLOutputFactory.newFactory().createXMLStreamWriter(stream, "UTF-8");
            xml.writeStartDocument("UTF-8", "1.0");
            xml.writeStartElement("project");
            element(xml, "pom", project.getFile().getAbsolutePath());
            element(xml, "mainOutput", project.getBuild().getOutputDirectory());
            element(xml, "testOutput", project.getBuild().getTestOutputDirectory());
            coverage(xml, CoverageResult.from(project));
            artifact(xml, project.getArtifact(), "main");
            for (var attached : project.getAttachedArtifacts())
                artifact(xml, attached, attached.getType().equals("test-jar") || "tests".equals(attached.getClassifier()) ? "test" : "main");
            context(xml, "main", project.getCompileSourceRoots(), project.getCompileClasspathElements());
            context(xml, "test", project.getTestCompileSourceRoots(), project.getTestClasspathElements());
            xml.writeEndElement();
            xml.writeEndDocument();
            xml.close();
        }
    }

    private static void coverage(XMLStreamWriter xml, CoverageResult coverage) throws XMLStreamException {
        if (coverage == null) return;
        xml.writeEmptyElement("coverage");
        if (coverage.version() != null) xml.writeAttribute("version", coverage.version());
        if (coverage.reason() != null) xml.writeAttribute("reason", coverage.reason());
        if (coverage.report() != null) {
            if (Files.isRegularFile(coverage.report())) xml.writeAttribute("report", coverage.report().toString());
            else xml.writeAttribute("reason", "JaCoCo produced no module report from this test run.");
        }
    }

    private static void artifact(XMLStreamWriter xml, Artifact artifact, String sourceSet) throws XMLStreamException {
        if (artifact.getFile() == null || !artifact.getFile().exists()) return;
        xml.writeStartElement("artifact");
        xml.writeAttribute("sourceSet", sourceSet);
        xml.writeCharacters(artifact.getFile().getAbsolutePath());
        xml.writeEndElement();
    }

    private static void context(XMLStreamWriter xml, String sourceSet, List<String> roots, List<String> classpath) throws XMLStreamException {
        xml.writeStartElement("context");
        xml.writeAttribute("sourceSet", sourceSet);
        for (var root : roots) element(xml, "sourceRoot", root);
        for (var entry : classpath) element(xml, "classpath", entry);
        xml.writeEndElement();
    }

    private static void element(XMLStreamWriter xml, String name, String value) throws XMLStreamException {
        xml.writeStartElement(name);
        xml.writeCharacters(value);
        xml.writeEndElement();
    }
}
