package cqc;

import java.io.IOException;
import java.util.*;
import java.util.jar.*;

/** Checks conflicting class definitions, allowing disjoint classifier JARs and identical bytecode. */
final class ClasspathConflicts {
    private record Owner(String artifact, String hash) { }
    private record Conflict(String className, String firstArtifact, String secondArtifact) { }

    static boolean detect(List<String> paths, MavenClasspath.Reporter reporter, String context) throws IOException {
        var classes = new HashMap<String, Owner>();
        boolean conflicts = false;
        for (String path : new LinkedHashSet<>(paths)) conflicts |= inspectArtifact(path, classes, reporter, context);
        return conflicts;
    }

    private static boolean inspectArtifact(String path, Map<String, Owner> classes, MavenClasspath.Reporter reporter, String context) throws IOException {
        boolean conflicts = false;
        try (var jar = new JarFile(path)) {
            for (var entry : Collections.list(jar.entries())) {
                if (!classEntry(entry.getName())) continue;
                var conflict = inspectClass(jar, entry, classes);
                if (conflict.isEmpty()) continue;
                conflicts = true;
                var found = conflict.get();
                reporter.report("java.classpath_conflict", "Context " + context + " resolves conflicting class " + found.className()
                    + " from " + found.firstArtifact() + " and " + found.secondArtifact() + ".", null);
            }
        }
        return conflicts;
    }

    private static Optional<Conflict> inspectClass(JarFile jar, JarEntry entry, Map<String, Owner> classes) throws IOException {
        String hash;
        try (var stream = jar.getInputStream(entry)) { hash = ClasspathArtifacts.hash(stream); }
        var prior = classes.putIfAbsent(entry.getName(), new Owner(jar.getName(), hash));
        if (prior == null || prior.hash().equals(hash)) return Optional.empty();
        return Optional.of(new Conflict(entry.getName(), prior.artifact(), jar.getName()));
    }

    private static boolean classEntry(String name) {
        if (!name.endsWith(".class")) return false;
        return !name.equals("module-info.class") && !name.endsWith("/module-info.class");
    }
    private ClasspathConflicts() { }
}
