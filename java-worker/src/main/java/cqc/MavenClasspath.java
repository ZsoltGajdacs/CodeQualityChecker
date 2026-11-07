package cqc;

import java.io.File;
import java.io.IOException;
import java.nio.file.*;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.*;
import java.util.jar.JarFile;
import javax.xml.XMLConstants;
import javax.xml.parsers.DocumentBuilderFactory;

/** Read-only classpath loading; Maven itself is owned by the host's explicit execution mode. */
final class MavenClasspath {
    interface Reporter { void report(String code, String message, String file); }
    private final Path root;
    private final String sourceFingerprint;
    private final Worker.Module[] modules;
    private final Reporter reporter;
    private boolean conflicts;

    MavenClasspath(Path root, String sourceFingerprint, Worker.Module[] modules, Reporter reporter) {
        this.root = root;
        this.sourceFingerprint = sourceFingerprint;
        this.modules = modules == null ? new Worker.Module[0] : modules;
        this.reporter = reporter;
    }

    boolean hasConflicts() { return conflicts; }

    SortedSet<String> read() throws IOException {
        var jars = new TreeSet<String>();
        if (!readGenerated(jars)) {
            for (var module : modules) {
                if (!module.language().equals("java") || !module.path().endsWith("pom.xml")) continue;
                Path pom = root.resolve(module.path()).normalize();
                if (pom.startsWith(root)) addCachedDependencies(pom, jars);
            }
        }
        detectConflicts(jars);
        return jars;
    }

    private boolean readGenerated(Set<String> jars) throws IOException {
        Path supplied = root.resolve(".cqc/java-classpath.txt");
        if (!Files.isRegularFile(supplied)) return false;
        Path provenance = Path.of(supplied + ".source");
        if (!validProvenance(provenance)) {
            reporter.report("java.stale_classpath", "Maven classpath provenance is missing or stale; explicitly execute analysis to refresh it.", null);
            return false;
        }
        Arrays.stream(Files.readString(supplied).split("[\\r\\n" + java.util.regex.Pattern.quote(File.pathSeparator) + "]+"))
            .filter(part -> !part.isBlank()).forEach(part -> addEntry(jars, part));
        return true;
    }

    private boolean validProvenance(Path path) throws IOException {
        return Files.isRegularFile(path) && Files.readString(path).trim().equals(sourceFingerprint);
    }

    private void addEntry(Set<String> jars, String part) {
        Path path = Path.of(part).isAbsolute() ? Path.of(part) : root.resolve(part);
        if (Files.isRegularFile(path) && part.endsWith(".jar")) jars.add(path.normalize().toString());
        else reporter.report("java.classpath", "Missing classpath entry " + part, null);
    }

    private void detectConflicts(Set<String> jars) throws IOException {
        var coordinates = new HashMap<String, String>();
        for (String path : jars) {
            try (var jar = new JarFile(path)) {
                String hash = hash(Path.of(path));
                for (var entry : Collections.list(jar.entries())) {
                    if (!entry.getName().startsWith("META-INF/maven/") || !entry.getName().endsWith("/pom.properties")) continue;
                    var properties = new Properties();
                    try (var stream = jar.getInputStream(entry)) { properties.load(stream); }
                    String coordinate = properties.getProperty("groupId", "") + ":" + properties.getProperty("artifactId", "");
                    if (coordinate.equals(":")) continue;
                    String identity = properties.getProperty("version", "") + ":" + hash;
                    var prior = coordinates.putIfAbsent(coordinate, identity);
                    if (prior != null && !prior.equals(identity)) {
                        conflicts = true;
                        reporter.report("java.classpath_conflict", "Modules resolve conflicting Maven binaries for " + coordinate + "; analyze those module roots separately for complete scores.", null);
                    }
                }
            }
        }
    }

    private static String hash(Path path) throws IOException {
        try {
            var digest = MessageDigest.getInstance("SHA-256");
            try (var stream = Files.newInputStream(path)) {
                byte[] bytes = new byte[8192];
                int count;
                while ((count = stream.read(bytes)) != -1) digest.update(bytes, 0, count);
            }
            return HexFormat.of().formatHex(digest.digest());
        } catch (NoSuchAlgorithmException exception) { throw new IllegalStateException(exception); }
    }

    private void addCachedDependencies(Path pom, Set<String> jars) {
        try {
            var document = factory().newDocumentBuilder().parse(pom.toFile());
            var properties = properties(document);
            var nodes = document.getElementsByTagName("dependency");
            for (int i = 0; i < nodes.getLength(); i++) {
                var dependency = (org.w3c.dom.Element) nodes.item(i);
                String group = child(dependency, "groupId");
                String artifact = child(dependency, "artifactId");
                String version = child(dependency, "version");
                for (var entry : properties.entrySet()) version = version.replace("${" + entry.getKey() + "}", entry.getValue());
                if (!resolvedCoordinate(group, artifact, version)) continue;
                Path cached = Path.of(System.getProperty("user.home"), ".m2", "repository", group.replace('.', '/'), artifact, version, artifact + "-" + version + ".jar");
                if (Files.isRegularFile(cached)) jars.add(cached.toString());
            }
        } catch (Exception exception) {
            reporter.report("java.maven_manifest", "Cannot inspect Maven dependencies in " + root.relativize(pom) + ": " + exception.getMessage(), root.relativize(pom).toString());
        }
    }

    private static boolean resolvedCoordinate(String group, String artifact, String version) {
        return List.of(group, artifact, version).stream().noneMatch(String::isEmpty) && !version.contains("${");
    }

    private static DocumentBuilderFactory factory() throws javax.xml.parsers.ParserConfigurationException {
        var factory = DocumentBuilderFactory.newInstance();
        factory.setFeature("http://apache.org/xml/features/disallow-doctype-decl", true);
        factory.setAttribute(XMLConstants.ACCESS_EXTERNAL_DTD, "");
        factory.setAttribute(XMLConstants.ACCESS_EXTERNAL_SCHEMA, "");
        return factory;
    }

    private static Map<String, String> properties(org.w3c.dom.Document document) {
        var result = new HashMap<String, String>();
        var propertyNodes = document.getElementsByTagName("properties");
        for (int i = 0; i < propertyNodes.getLength(); i++) {
            var children = propertyNodes.item(i).getChildNodes();
            for (int j = 0; j < children.getLength(); j++) if (children.item(j).getNodeType() == org.w3c.dom.Node.ELEMENT_NODE)
                result.put(children.item(j).getNodeName(), children.item(j).getTextContent().trim());
        }
        return result;
    }

    private static String child(org.w3c.dom.Element element, String name) {
        var nodes = element.getElementsByTagName(name);
        return nodes.getLength() == 0 ? "" : nodes.item(0).getTextContent().trim();
    }
}
