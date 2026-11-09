package cqc.metadata;

import org.apache.maven.eventspy.AbstractEventSpy;
import org.apache.maven.execution.ExecutionEvent;
import org.apache.maven.project.MavenProject;
import org.apache.maven.artifact.Artifact;
import java.nio.file.*;
import java.util.*;
import javax.xml.stream.*;

/** Captures Maven's evaluated project model at the final dependency-resolution goal of each module. */
public final class MetadataSpy extends AbstractEventSpy {
    @Override public void onEvent(Object event) throws Exception {
        if (!(event instanceof ExecutionEvent execution) || execution.getType() != ExecutionEvent.Type.MojoSucceeded) return;
        var mojo = execution.getMojoExecution();
        if (!mojo.getArtifactId().equals("maven-dependency-plugin") || !mojo.getGoal().equals("build-classpath")) return;
        writeProject(execution.getProject());
    }

    private static void writeProject(MavenProject project) throws Exception {
        Path output = Path.of(project.getBuild().getDirectory(), "cqc-metadata.xml");
        Files.createDirectories(output.getParent());
        try (var stream = Files.newOutputStream(output)) {
            var xml = XMLOutputFactory.newFactory().createXMLStreamWriter(stream, "UTF-8");
            xml.writeStartDocument("UTF-8", "1.0");
            xml.writeStartElement("project");
            element(xml, "pom", project.getFile().getAbsolutePath());
            element(xml, "buildDirectory", project.getBuild().getDirectory());
            element(xml, "mainOutput", project.getBuild().getOutputDirectory());
            element(xml, "testOutput", project.getBuild().getTestOutputDirectory());
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
