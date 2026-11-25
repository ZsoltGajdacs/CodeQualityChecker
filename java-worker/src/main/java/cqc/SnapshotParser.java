package cqc;

import com.github.javaparser.JavaParser;
import com.github.javaparser.ParserConfiguration;
import java.io.IOException;
import java.nio.file.*;
import java.util.*;

/** Parses authored sources and assigns their Maven contexts. */
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
        var resolver = new ContextResolver(root, reporter, new ResolutionSources(contexts, sources), symbols);
        var effective = new LinkedHashMap<String, ContextManifest.Context>();
        for (var context : contexts) effective.put(context.id(), resolver.configure(context));
        var configuredSources = sources.stream().map(source -> new Worker.Source(source.file(), source.unit(), effective.get(source.context().id()))).toList();
        return new Context(configuredSources, List.copyOf(effective.values()), List.copyOf(gaps), symbols);
    }

    private static boolean javaSource(String file) {
        return file.toLowerCase(Locale.ROOT).endsWith(".java");
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

    static ParserConfiguration configuration() {
        return new ParserConfiguration().setLanguageLevel(ParserConfiguration.LanguageLevel.BLEEDING_EDGE);
    }
}
