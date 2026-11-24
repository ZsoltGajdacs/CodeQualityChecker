package cqc;

import java.nio.file.*;
import java.util.*;

/** Assigns authored sources to declared contexts, or conservative source-only contexts. */
final class SourceContexts {
    private final Path root;
    private final MavenClasspath.Reporter reporter;
    private final List<ContextManifest.Context> contexts;
    private final Worker.Module[] modules;
    private final boolean sourceOnly;

    SourceContexts(Path root, List<ContextManifest.Context> contexts, Worker.Module[] modules, MavenClasspath.Reporter reporter, boolean sourceOnly) {
        this.root = root;
        this.contexts = new ArrayList<>(contexts);
        this.modules = modules == null ? new Worker.Module[0] : modules;
        this.reporter = reporter;
        this.sourceOnly = sourceOnly;
    }

    ContextManifest.Context forSource(Worker.Source source) {
        return declaredContext(Path.of(source.file())).orElseGet(() -> fallback(source));
    }

    private Optional<ContextManifest.Context> declaredContext(Path file) {
        return contexts.stream().filter(context -> !Arrays.asList(context.blockers()).contains("context.unverified"))
            .filter(context -> Arrays.stream(context.roots()).anyMatch(path -> under(file, Path.of(path))))
            .max(Comparator.comparingInt(context -> longestRoot(context, file)));
    }

    private ContextManifest.Context fallback(Worker.Source source) {
        String module = portable(nearestModule(Path.of(source.file())));
        String set = testSource(Path.of(source.file())) ? "test" : "main";
        String id = module + "#" + set;
        var existing = contexts.stream().filter(context -> context.id().equals(id)).findFirst();
        if (existing.isPresent()) return unverified(existing.get(), source);
        var definition = new ContextManifest.Definition(id, module, set, new String[]{sourceRoot(source)}, new ContextManifest.Entry[0],
            set.equals("test") ? new String[]{module + "#main"} : new String[0]);
        var artifacts = sourceOnly ? List.<ClasspathArtifact>of() : MavenFallback.artifacts(root, module, reporter);
        var context = new ContextManifest.Context(definition, artifacts, false, new String[]{"context.unverified"});
        contexts.add(context);
        if (!sourceOnly)
            reporter.report("java.context_unverified", "No verified Maven metadata for " + id + "; semantic scores require explicit execution.", module);
        return context;
    }

    private ContextManifest.Context unverified(ContextManifest.Context found, Worker.Source source) {
        if (!found.verified()) return found;
        var invalid = new ContextManifest.Context(found.definition(), found.artifacts(), false, new String[]{"context.unverified"});
        contexts.set(contexts.indexOf(found), invalid);
        reporter.report("java.context_unverified", "Source " + source.file() + " is outside evaluated roots for " + found.id() + ".", source.file());
        return invalid;
    }

    String contextId(String file) {
        Path path = Path.of(file);
        return declaredContext(path).map(ContextManifest.Context::id)
            .orElseGet(() -> portable(nearestModule(path)) + (testSource(path) ? "#test" : "#main"));
    }

    private Path nearestModule(Path file) {
        return Arrays.stream(modules).filter(module -> "java".equals(module.language()) && module.path().endsWith("pom.xml"))
            .map(module -> Path.of(module.path())).filter(pom -> under(file, parent(pom)))
            .max(Comparator.comparingInt(Path::getNameCount)).orElse(Path.of("pom.xml"));
    }

    static String sourceRoot(Worker.Source source) {
        Path result = Path.of(source.file()).getParent();
        var packageName = source.unit().getPackageDeclaration().map(p -> p.getNameAsString()).orElse("");
        int segments = packageName.isEmpty() ? 0 : packageName.split("\\.").length;
        for (int i = 0; i < segments && result != null; i++) result = result.getParent();
        return result == null ? "." : portable(result);
    }

    private static boolean testSource(Path file) { return ("/" + portable(file)).contains("/src/test/"); }
    private static Path parent(Path path) { return path.getParent() == null ? Path.of("") : path.getParent(); }
    private static boolean under(Path file, Path directory) { return directory.normalize().toString().isEmpty() || file.normalize().startsWith(directory.normalize()); }
    private static String portable(Path path) { return path.toString().replace('\\', '/'); }
    private static int longestRoot(ContextManifest.Context context, Path file) {
        return Arrays.stream(context.roots()).map(Path::of).filter(path -> under(file, path)).mapToInt(Path::getNameCount).max().orElse(0);
    }
    List<ContextManifest.Context> contexts() { return List.copyOf(contexts); }
}
