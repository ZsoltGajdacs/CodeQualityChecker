package cqc;

import com.github.javaparser.JavaParser;
import com.github.javaparser.ParserConfiguration;
import com.github.javaparser.symbolsolver.JavaSymbolSolver;
import com.github.javaparser.symbolsolver.resolution.typesolvers.*;
import java.io.IOException;
import java.nio.file.*;
import java.util.*;

final class SnapshotParser {
    private final Worker.Snapshot input;
    private final Path root;
    private final MavenClasspath.Reporter reporter;
    record Context(List<Worker.Source> sources, boolean ambiguousClasspath) { }

    SnapshotParser(Worker.Snapshot input, MavenClasspath.Reporter reporter) {
        this.input = input;
        this.root = Path.of(input.root()).toAbsolutePath().normalize();
        this.reporter = reporter;
    }

    Context load() throws IOException {
        var parser = new JavaParser(configuration());
        var sources = new ArrayList<Worker.Source>();
        for (var file : Arrays.stream(input.files()).filter(f -> f.endsWith(".java")).sorted().toList())
            parseFile(parser, file).ifPresent(sources::add);
        var solver = new CombinedTypeSolver(new ReflectionTypeSolver(ReflectionTypeSolver.JCL_ONLY));
        sources.stream().map(this::sourceRoot).filter(Objects::nonNull).distinct().sorted()
            .forEach(path -> solver.add(new JavaParserTypeSolver(path, configuration())));
        var classpath = new MavenClasspath(root, input.sourceFingerprint(), input.modules(), reporter);
        for (var jar : classpath.read()) addJar(solver, jar);
        var resolver = new JavaSymbolSolver(solver);
        sources.forEach(source -> resolver.inject(source.unit()));
        return new Context(sources, classpath.hasConflicts());
    }

    private Optional<Worker.Source> parseFile(JavaParser parser, String file) throws IOException {
        Path path = root.resolve(file).normalize();
        if (!path.startsWith(root) || !path.toRealPath().startsWith(root.toRealPath()))
            throw new IllegalArgumentException("Source outside project root.");
        var result = parser.parse(path);
        if (!result.isSuccessful()) {
            reporter.report("java.parse", result.getProblems().toString(), file);
            return Optional.empty();
        }
        return result.getResult().map(unit -> new Worker.Source(file, unit));
    }

    private Path sourceRoot(Worker.Source source) {
        Path result = root.resolve(source.file()).getParent();
        var packageName = source.unit().getPackageDeclaration().map(p -> p.getNameAsString()).orElse("");
        int segments = packageName.isEmpty() ? 0 : packageName.split("\\.").length;
        for (int i = 0; i < segments && result != null; i++) result = result.getParent();
        return result != null && result.startsWith(root) ? result : null;
    }

    private void addJar(CombinedTypeSolver solver, String jar) {
        try { solver.add(new JarTypeSolver(jar)); }
        catch (Exception exception) { reporter.report("java.classpath", "Cannot load dependency " + Path.of(jar).getFileName(), null); }
    }

    private static ParserConfiguration configuration() {
        return new ParserConfiguration().setLanguageLevel(ParserConfiguration.LanguageLevel.BLEEDING_EDGE);
    }
}
