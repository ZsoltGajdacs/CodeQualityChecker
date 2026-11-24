package cqc;

import java.io.IOException;
import java.nio.file.Path;
import java.util.*;

/** Verifies one context's classpath in order and isolates invalid binary evidence. */
final class ClasspathArtifacts {
    private final Path root;
    private final MavenClasspath.Reporter reporter;

    ClasspathArtifacts(Path root, MavenClasspath.Reporter reporter) { this.root = root; this.reporter = reporter; }

    ContextManifest.Context load(ContextManifest.Definition definition) {
        var entries = new LinkedHashMap<ClasspathArtifact, List<ClasspathArtifact.ClassDigest>>();
        var blockers = new TreeSet<String>();
        for (var entry : definition.classpath()) {
            var artifact = new ClasspathArtifact(root.resolve(entry.path()).normalize(), entry.kind());
            try {
                var classes = artifact.verifiedClasses(entry.sha256());
                entries.putIfAbsent(artifact, classes);
            } catch (IOException exception) {
                blockers.add("context.artifactChanged");
                reporter.report("java.classpath_invalid", definition.id() + ": " + exception.getMessage(), definition.modulePath());
            }
        }
        if (ClasspathConflicts.detect(entries, reporter, definition.id())) blockers.add("context.classpathConflict");
        return new ContextManifest.Context(definition, List.copyOf(entries.keySet()), blockers.isEmpty(), blockers.toArray(String[]::new));
    }
}
