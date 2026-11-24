package cqc;

import java.nio.file.Path;
import java.util.*;

/** Validates the manifest shape once before contexts reach parser setup. */
final class ManifestValidation {
    static void validate(ContextManifest.Manifest manifest, String fingerprint, Path root) {
        validateHeader(manifest, fingerprint);
        validateContexts(manifest.contexts(), root);
    }

    private static void validateHeader(ContextManifest.Manifest manifest, String fingerprint) {
        Objects.requireNonNull(manifest, "Module manifest is required.");
        if (!"3".equals(manifest.schemaVersion())) throw new IllegalArgumentException("Incompatible module manifest schema; expected 3.");
        if (!fingerprint.equals(manifest.sourceFingerprint())) throw new IllegalArgumentException("Stale module metadata.");
        Objects.requireNonNull(manifest.contexts(), "Module contexts are required.");
    }

    private static void validateContexts(ContextManifest.Definition[] definitions, Path root) {
        var ids = new HashSet<String>();
        for (var definition : definitions) validateContext(definition, ids, root);
        for (var definition : definitions) validateReachability(definition, ids);
    }

    private static void validateContext(ContextManifest.Definition definition, Set<String> ids, Path root) {
        Objects.requireNonNull(definition, "Module context is required.");
        Objects.requireNonNull(definition.modulePath(), "Module path is required.");
        Objects.requireNonNull(definition.sourceSet(), "Source set is required.");
        if (!Set.of("main", "test").contains(definition.sourceSet())) throw new IllegalArgumentException("Invalid source set.");
        if (!(definition.modulePath() + "#" + definition.sourceSet()).equals(definition.id())) throw new IllegalArgumentException("Invalid context identity.");
        if (!ids.add(definition.id())) throw new IllegalArgumentException("Duplicate context identity.");
        insideRoot(root, definition.modulePath());
        validatePaths(definition, root);
    }

    private static void validatePaths(ContextManifest.Definition definition, Path root) {
        Objects.requireNonNull(definition.sourceRoots(), "Source roots are required.");
        for (String sourceRoot : definition.sourceRoots()) insideRoot(root, sourceRoot);
        Objects.requireNonNull(definition.classpath(), "Classpath is required.");
        for (var entry : definition.classpath()) validateEntry(entry);
    }

    private static void validateReachability(ContextManifest.Definition definition, Set<String> ids) {
        Objects.requireNonNull(definition.reachableContexts(), "Reachable contexts are required.");
        for (String reachable : definition.reachableContexts()) validateReference(definition, reachable, ids);
    }

    private static void validateReference(ContextManifest.Definition definition, String reachable, Set<String> ids) {
        if (!ids.contains(reachable)) throw new IllegalArgumentException("Unknown reachable context " + reachable);
        if (definition.sourceSet().equals("main") && reachable.equals(definition.modulePath() + "#test"))
            throw new IllegalArgumentException("Main sources cannot depend on their own test sources.");
    }

    private static void validateEntry(ContextManifest.Entry entry) {
        Objects.requireNonNull(entry, "Classpath artifact is required.");
        Objects.requireNonNull(entry.path(), "Artifact path is required.");
        Objects.requireNonNull(entry.kind(), "Artifact kind must be jar or directory.");
        Objects.requireNonNull(entry.sha256(), "Artifact hash is required.");
        if (!entry.sha256().matches("[0-9a-f]{64}")) throw new IllegalArgumentException("Invalid artifact hash.");
    }

    private static void insideRoot(Path root, String relative) {
        Objects.requireNonNull(relative, "Relative path is required.");
        if (Path.of(relative).isAbsolute()) throw new IllegalArgumentException("Module/source root must be relative.");
        if (!root.resolve(relative).normalize().startsWith(root)) throw new IllegalArgumentException("Module/source root escapes project.");
    }
    private ManifestValidation() { }
}
