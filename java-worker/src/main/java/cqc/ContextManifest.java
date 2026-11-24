package cqc;

import com.google.gson.Gson;
import java.io.IOException;
import java.nio.file.*;
import java.util.*;

/** Reads provenance-checked module metadata without executing Maven. */
final class ContextManifest {
    record Entry(String path, ClasspathArtifact.Kind kind, String sha256) { }
    record Definition(String id, String modulePath, String sourceSet, String[] sourceRoots, Entry[] classpath, String[] reachableContexts) { }
    record Manifest(String schemaVersion, String sourceFingerprint, Definition[] contexts) { }
    record Context(Definition definition, List<ClasspathArtifact> artifacts, boolean verified, String[] blockers) {
        String id() { return definition.id(); }
        String modulePath() { return definition.modulePath(); }
        String sourceSet() { return definition.sourceSet(); }
        String[] roots() { return definition.sourceRoots(); }
        String[] reachable() { return definition.reachableContexts(); }
        Worker.AnalysisContext fact() { return new Worker.AnalysisContext(id(), modulePath(), sourceSet(), reachable(), verified, blockers); }
    }
    private final Path root;
    private final MavenClasspath.Reporter reporter;

    ContextManifest(Path root, MavenClasspath.Reporter reporter) { this.root = root; this.reporter = reporter; }

    List<Context> read(String fingerprint, String stagedDirectory) throws IOException {
        Path directory = stagedDirectory == null ? root.resolve(".cqc") : Path.of(stagedDirectory);
        Path path = directory.resolve("java-contexts.json");
        if (!Files.isRegularFile(path)) return stagedDirectory == null ? missing() : List.of();
        try {
            var manifest = new Gson().fromJson(Files.readString(path), Manifest.class);
            ManifestValidation.validate(manifest, fingerprint, root);
            var artifacts = new ClasspathArtifacts(root, reporter);
            return Arrays.stream(manifest.contexts()).map(artifacts::load).toList();
        } catch (RuntimeException exception) {
            reporter.report("java.stale_classpath", "Cannot accept module metadata: " + exception.getMessage() + " Execute analysis to refresh it.", null);
            return List.of();
        }
    }

    private List<Context> missing() {
        if (Files.exists(root.resolve(".cqc/java-classpath.txt")))
            reporter.report("java.legacy_classpath", "Legacy Java classpath evidence is unsupported; execute analysis to refresh module contexts.", null);
        return List.of();
    }
}
