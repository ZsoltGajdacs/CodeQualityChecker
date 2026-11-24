package cqc;

import com.github.javaparser.JavaParser;
import com.github.javaparser.ParserConfiguration;
import com.github.javaparser.symbolsolver.JavaSymbolSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.*;
import java.io.IOException;
import java.nio.file.*;
import java.util.*;

/** Owns parser/resolver lifetime; fact extraction does not configure classpaths. */
final class SnapshotParser {
    private final Worker.Snapshot input;
    private final Path root;
    private final MavenClasspath.Reporter reporter;
    private final List<Worker.SyntaxGap> gaps = new ArrayList<>();
    record Context(List<Worker.Source> sources, List<ContextManifest.Context> contexts, List<Worker.SyntaxGap> gaps, SymbolContexts symbols) { }

    SnapshotParser(Worker.Snapshot input, MavenClasspath.Reporter reporter) {
        this.input = input;
        this.root = Path.of(input.root()).toAbsolutePath().normalize();
        this.reporter = reporter;
    }

    Context load() throws IOException {
        var manifest = input.sourceOnly() ? List.<ContextManifest.Context>of()
            : new ContextManifest(root, reporter).read(input.sourceFingerprint(), input.referenceEvidenceDirectory());
        var definitions = new SourceContexts(root, manifest, input.modules(), reporter, input.sourceOnly());
        var sources = new ArrayList<Worker.Source>();
        var parser = new JavaParser(configuration());
        for (String file : Arrays.stream(input.files()).filter(SnapshotParser::javaSource).sorted().toList()) {
            var source = parseFile(parser, file, definitions.contextId(file));
            if (source.isPresent()) {
                var parsed = source.get();
                sources.add(new Worker.Source(file, parsed.unit(), definitions.forSource(parsed)));
            }
        }
        var contexts = new ArrayList<>(definitions.contexts());
        var symbols = new SymbolContexts(sources, contexts);
        if (input.sourceOnly()) return new Context(sources, contexts, List.copyOf(gaps), symbols);
        var index = new ResolutionSources(contexts, sources);
        for (int position = 0; position < contexts.size(); position++)
            contexts.set(position, inject(contexts.get(position), index, symbols));
        var effective = contexts.stream().collect(java.util.stream.Collectors.toMap(ContextManifest.Context::id, context -> context));
        var configuredSources = sources.stream().map(source -> new Worker.Source(source.file(), source.unit(), effective.get(source.context().id()))).toList();
        return new Context(configuredSources, contexts, List.copyOf(gaps), symbols);
    }

    private static boolean javaSource(String file) {
        return file.toLowerCase(Locale.ROOT).endsWith(".java");
    }

    private ContextManifest.Context inject(ContextManifest.Context context, ResolutionSources sources, SymbolContexts symbols) {
        var solver = new CombinedTypeSolver(new ReflectionTypeSolver(ReflectionTypeSolver.JCL_ONLY));
        Set<String> visible = symbols.visible(context.id());
        var roots = sources.roots(visible);
        for (String sourceRoot : roots) {
            Path path = root.resolve(sourceRoot).normalize();
            if (path.startsWith(root) && Files.isDirectory(path)) solver.add(new JavaParserTypeSolver(path, configuration()));
        }
        boolean complete = true;
        for (var artifact : context.artifacts()) complete &= addArtifact(solver, artifact, context);
        var resolver = new JavaSymbolSolver(solver);
        sources.owned(context.id()).forEach(source -> resolver.inject(source.unit()));
        if (complete) return context;
        var blockers = new TreeSet<>(Arrays.asList(context.blockers()));
        blockers.add("context.artifactChanged");
        return new ContextManifest.Context(context.definition(), context.artifacts(), false, blockers.toArray(String[]::new));
    }

    private Optional<Worker.Source> parseFile(JavaParser parser, String file, String contextId) throws IOException {
        Path path = root.resolve(file).normalize();
        if (!path.startsWith(root) || !path.toRealPath().startsWith(root.toRealPath()))
            throw new IllegalArgumentException("Source outside project root.");
        var result = parser.parse(path);
        if (!result.isSuccessful()) {
            reporter.report("java.parse", result.getProblems().toString(), file);
            var location = result.getProblems().stream().flatMap(problem -> problem.getLocation().stream())
                .flatMap(tokens -> tokens.toRange().stream()).findFirst()
                .map(range -> new Worker.Location(file, range.begin.line, range.end.line)).orElse(null);
            gaps.add(new Worker.SyntaxGap(file, "java", contextId, "syntax.parse", true, location, null));
            return Optional.empty();
        }
        return result.getResult().map(unit -> new Worker.Source(file, unit, null));
    }

    private boolean addArtifact(CombinedTypeSolver solver, ClasspathArtifact artifact, ContextManifest.Context context) {
        // JavaParser's bytecode solver also accepts compiled class directories directly.
        try { solver.add(new JarTypeSolver(artifact.path())); return true; }
        catch (Exception exception) {
            reporter.report("java.classpath", "Cannot load dependency " + artifact.path().getFileName() + " in " + context.id(), context.modulePath());
            return false;
        }
    }

    private static ParserConfiguration configuration() {
        return new ParserConfiguration().setLanguageLevel(ParserConfiguration.LanguageLevel.BLEEDING_EDGE);
    }
}
