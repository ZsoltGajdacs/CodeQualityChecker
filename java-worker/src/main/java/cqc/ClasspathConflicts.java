package cqc;

import java.util.*;

/** Checks conflicting class definitions, allowing disjoint artifacts and identical bytecode. */
final class ClasspathConflicts {
    private record Owner(ClasspathArtifact artifact, byte[] hash) { }

    static boolean detect(Map<ClasspathArtifact, List<ClasspathArtifact.ClassDigest>> artifacts, MavenClasspath.Reporter reporter, String context) {
        var owners = new HashMap<String, Owner>();
        boolean conflicts = false;
        for (var artifact : artifacts.entrySet()) {
            for (var entry : artifact.getValue()) {
                if (!entry.canConflict()) continue;
                var previous = owners.putIfAbsent(entry.name(), new Owner(artifact.getKey(), entry.sha256()));
                if (previous == null || Arrays.equals(previous.hash(), entry.sha256())) continue;
                conflicts = true;
                reporter.report("java.classpath_conflict", "Context " + context + " resolves conflicting class " + entry.name()
                    + " from " + previous.artifact().path() + " and " + artifact.getKey().path() + ".", null);
            }
        }
        return conflicts;
    }

    private ClasspathConflicts() { }
}
