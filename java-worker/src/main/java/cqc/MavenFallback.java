package cqc;

import java.nio.file.*;
import java.util.*;
import javax.xml.XMLConstants;
import javax.xml.parsers.DocumentBuilderFactory;

/** Raw POM inspection is a best-effort fallback and never verified Maven evidence. */
final class MavenFallback {
    static List<String> jars(Path root, String module, MavenClasspath.Reporter reporter) {
        var jars = new ArrayList<String>();
        Path pom = root.resolve(module);
        if (!Files.isRegularFile(pom)) return jars;
        try {
            var factory = DocumentBuilderFactory.newInstance();
            factory.setFeature("http://apache.org/xml/features/disallow-doctype-decl", true);
            factory.setAttribute(XMLConstants.ACCESS_EXTERNAL_DTD, "");
            factory.setAttribute(XMLConstants.ACCESS_EXTERNAL_SCHEMA, "");
            var document = factory.newDocumentBuilder().parse(pom.toFile());
            var nodes = document.getElementsByTagName("dependency");
            for (int i = 0; i < nodes.getLength(); i++) {
                var dependency = (org.w3c.dom.Element)nodes.item(i);
                cachedDependency(dependency).ifPresent(jars::add);
            }
        } catch (Exception exception) {
            reporter.report("java.maven_manifest", "Cannot inspect " + module + ": " + exception.getMessage(), module);
        }
        return jars;
    }
    private static Optional<String> cachedDependency(org.w3c.dom.Element dependency) {
        String group = child(dependency, "groupId"), artifact = child(dependency, "artifactId"), version = child(dependency, "version");
        if (List.of(group, artifact, version).stream().anyMatch(String::isBlank)) return Optional.empty();
        if (version.contains("${")) return Optional.empty();
        Path path = Path.of(System.getProperty("user.home"), ".m2", "repository", group.replace('.', '/'), artifact, version, artifact + "-" + version + ".jar");
        return Files.isRegularFile(path) ? Optional.of(path.toString()) : Optional.empty();
    }
    private static String child(org.w3c.dom.Element parent, String name) {
        for (var node = parent.getFirstChild(); node != null; node = node.getNextSibling())
            if (node instanceof org.w3c.dom.Element element && element.getTagName().equals(name)) return element.getTextContent().trim();
        return "";
    }
    private MavenFallback() { }
}
