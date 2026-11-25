package cqc;

import com.github.javaparser.symbolsolver.JavaSymbolSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.*;
import java.nio.file.*;
import java.util.*;

/** Configures resolution without replacing authored syntax trees. */
final class ContextResolver {
    private final Path root;
    private final MavenClasspath.Reporter reporter;
    private final ResolutionSources sources;
    private final SymbolContexts symbols;

    ContextResolver(Path root, MavenClasspath.Reporter reporter, ResolutionSources sources, SymbolContexts symbols) {
        this.root = root;
        this.reporter = reporter;
        this.sources = sources;
        this.symbols = symbols;
    }

    ContextManifest.Context configure(ContextManifest.Context context) {
        var solver = new CombinedTypeSolver(new ReflectionTypeSolver(ReflectionTypeSolver.JCL_ONLY));
        var roots = sources.roots(symbols.visible(context.id()));
        // Verified bytecode includes generated members that the authored type declaration lacks.
        if (!context.verified()) addSources(solver, roots);
        boolean complete = addArtifacts(solver, context);
        if (context.verified()) addSources(solver, roots);

        var resolver = new JavaSymbolSolver(solver);
        sources.owned(context.id()).forEach(source -> resolver.inject(source.unit()));
        if (complete) return context;
        var blockers = new TreeSet<>(Arrays.asList(context.blockers()));
        blockers.add("context.artifactChanged");
        return new ContextManifest.Context(context.definition(), context.artifacts(), false, blockers.toArray(String[]::new));
    }

    private void addSources(CombinedTypeSolver solver, Set<String> roots) {
        for (String sourceRoot : roots) {
            Path path = root.resolve(sourceRoot).normalize();
            if (path.startsWith(root) && Files.isDirectory(path))
                solver.add(new JavaParserTypeSolver(path, SnapshotParser.configuration()));
        }
    }

    private boolean addArtifacts(CombinedTypeSolver solver, ContextManifest.Context context) {
        boolean complete = true;
        for (var artifact : context.artifacts()) {
            try {
                // The bytecode solver accepts compiled class directories as well as jars.
                solver.add(new JarTypeSolver(artifact.path()));
            } catch (Exception exception) {
                reporter.report("java.classpath", "Cannot load dependency " + artifact.path().getFileName()
                    + " in " + context.id(), context.modulePath());
                complete = false;
            }
        }
        return complete;
    }
}
