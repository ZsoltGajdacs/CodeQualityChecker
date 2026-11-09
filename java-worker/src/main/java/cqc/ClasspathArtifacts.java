package cqc;

import java.io.IOException;
import java.io.InputStream;
import java.nio.file.*;
import java.security.*;
import java.util.*;

/** Verifies and loads one context's artifacts, isolating invalid binary evidence. */
final class ClasspathArtifacts {
    private final Path root;
    private final MavenClasspath.Reporter reporter;

    ClasspathArtifacts(Path root, MavenClasspath.Reporter reporter) { this.root = root; this.reporter = reporter; }

    ContextManifest.Context load(ContextManifest.Definition definition) {
        var jars = new ArrayList<String>();
        var blockers = new TreeSet<String>();
        for (var entry : definition.classpath()) readEntry(entry, definition, jars, blockers);
        detectConflicts(definition, jars, blockers);
        return new ContextManifest.Context(definition, List.copyOf(jars), blockers.isEmpty(), blockers.toArray(String[]::new));
    }

    private void readEntry(ContextManifest.Entry entry, ContextManifest.Definition definition, List<String> jars, Set<String> blockers) {
        Path path = root.resolve(entry.path()).normalize();
        try {
            if (!Files.isRegularFile(path)) throw new IOException("Missing artifact " + path.getFileName());
            if (!path.toString().endsWith(".jar")) throw new IOException("Artifact must be a JAR: " + path.getFileName());
            if (!hash(path).equals(entry.sha256())) throw new IOException("Changed artifact " + path.getFileName());
            jars.add(path.toString());
        } catch (IOException exception) { invalid(definition, blockers, exception); }
    }

    private void detectConflicts(ContextManifest.Definition definition, List<String> jars, Set<String> blockers) {
        try {
            if (ClasspathConflicts.detect(jars, reporter, definition.id())) blockers.add("context.classpathConflict");
        } catch (IOException exception) { invalid(definition, blockers, exception); }
    }

    private void invalid(ContextManifest.Definition definition, Set<String> blockers, IOException exception) {
        blockers.add("context.artifactChanged");
        reporter.report("java.classpath_invalid", definition.id() + ": " + exception.getMessage(), definition.modulePath());
    }

    static String hash(Path path) throws IOException {
        try (var stream = Files.newInputStream(path)) { return hash(stream); }
    }

    static String hash(InputStream stream) throws IOException {
        try {
            var digest = MessageDigest.getInstance("SHA-256");
            byte[] bytes = new byte[8192];
            int count;
            while ((count = stream.read(bytes)) != -1) digest.update(bytes, 0, count);
            return HexFormat.of().formatHex(digest.digest());
        } catch (NoSuchAlgorithmException exception) { throw new IllegalStateException(exception); }
    }
}
