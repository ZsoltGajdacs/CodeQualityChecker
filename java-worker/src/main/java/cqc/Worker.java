package cqc;

import com.github.javaparser.ast.CompilationUnit;
import com.github.javaparser.ast.body.*;
import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import static cqc.AstIdentity.*;

/** One input snapshot and one output fact set; no target build tools are invoked. */
public final class Worker {
    private static final Gson JSON = new GsonBuilder().serializeNulls().create();
    private static final Set<Class<?>> UNMODELED_EXECUTABLE = Set.of(CompactConstructorDeclaration.class, InitializerDeclaration.class);
    private final List<TypeFact> types = new ArrayList<>();
    private final List<MethodFact> methods = new ArrayList<>();
    private final List<DependencyFact> dependencies = new ArrayList<>();
    private final List<Diagnostic> diagnostics = new ArrayList<>();
    private final Set<String> reported = new HashSet<>();
    private boolean semanticComplete = true;
    private boolean ambiguousClasspath;
    private MethodAnalyzer methodAnalyzer;

    record Module(String language, String path) { }
    record Snapshot(String root, String[] files, String sourceFingerprint, Module[] modules, String schemaVersion) { }
    record Location(String file, int startLine, int endLine) { }
    record Diagnostic(String code, String message, String file, String language) { }
    record TypeFact(String id, String language, Location location, boolean isAbstract, boolean isTest, boolean complete) { }
    record DependencyFact(String from, String to, Location location) { }
    record MethodFact(String id, String typeId, String language, Location location, int complexity,
        int executableLines, boolean isTest, boolean isFixture, boolean isParameterized,
        int assertions, String[] calls, String[] fingerprints, int nodeCount, boolean complete, String coverageSignature) { }
    record AnalysisFacts(List<TypeFact> types, List<MethodFact> methods, List<DependencyFact> dependencies,
        List<Diagnostic> diagnostics, boolean semanticComplete, String schemaVersion) { }
    record Source(String file, CompilationUnit unit) { }

    public static void main(String[] args) {
        try {
            var input = JSON.fromJson(new InputStreamReader(System.in, StandardCharsets.UTF_8), Snapshot.class);
            Objects.requireNonNull(input, "Expected a snapshot object.");
            Objects.requireNonNull(input.root(), "Snapshot root is required.");
            Objects.requireNonNull(input.files(), "Snapshot files are required.");
            if (!"1".equals(input.schemaVersion())) throw new IllegalArgumentException("Unsupported snapshot schemaVersion; expected 1.");
            System.out.print(JSON.toJson(new Worker().analyze(input)));
        } catch (Exception exception) {
            System.err.println(exception.getClass().getSimpleName() + ": " + exception.getMessage());
            System.exit(1);
        }
    }


    private AnalysisFacts analyze(Snapshot input) throws IOException {
        var context = new SnapshotParser(input, this::incomplete).load();
        ambiguousClasspath = context.ambiguousClasspath();
        methodAnalyzer = new MethodAnalyzer(this::incomplete, ambiguousClasspath);
        for (var source : context.sources())
            for (var type : source.unit().findAll(TypeDeclaration.class)) analyzeType(source, type);
        sortResults();
        return new AnalysisFacts(types, methods, dependencies, diagnostics, semanticComplete, "1");
    }

    private void analyzeType(Source source, TypeDeclaration<?> type) {
        var edges = new DependencyAnalyzer(this::incomplete, source).analyze(type);
        dependencies.addAll(edges.edges());
        boolean isTest = testSource(source.file()) || type.getMethods().stream().anyMatch(TestRecognition::testAnnotation);
        boolean isAbstract = type instanceof ClassOrInterfaceDeclaration declaration && abstractType(declaration);
        boolean unsupported = unsupportedExecutable(source, type);
        types.add(new TypeFact(typeName(type), "java", location(source.file(), type), isAbstract, isTest,
            edges.complete() && !ambiguousClasspath && !unsupported));
        type.getMembers().forEach(member -> analyzeMember(source, type, member));
    }

    private static boolean abstractType(ClassOrInterfaceDeclaration type) { return type.isInterface() || type.isAbstract(); }

    private static boolean testSource(String file) {
        String normalized = "/" + file.replace('\\', '/');
        return normalized.contains("/src/test/");
    }

    private boolean unsupportedExecutable(Source source, TypeDeclaration<?> type) {
        boolean unsupported = type.getMembers().stream().anyMatch(member -> UNMODELED_EXECUTABLE.contains(member.getClass()));
        if (unsupported) incomplete("java.unmodeled_executable", "Compact record constructors and initializer blocks are not modeled yet; scores for " + typeName(type) + " are unavailable.", source.file());
        return unsupported;
    }

    private void analyzeMember(Source source, TypeDeclaration<?> type, BodyDeclaration<?> member) {
        if (member instanceof MethodDeclaration method)
            method.getBody().ifPresent(body -> methods.add(methodAnalyzer.analyze(source, typeName(type), method, body)));
        if (member instanceof ConstructorDeclaration constructor)
            methods.add(methodAnalyzer.analyze(source, typeName(type), constructor, constructor.getBody()));
    }

    private void sortResults() {
        types.sort(Comparator.comparing(TypeFact::id).thenComparing(t -> t.location().file()).thenComparingInt(t -> t.location().startLine()));
        methods.sort(Comparator.comparing(MethodFact::id).thenComparing(m -> m.location().file()));
        dependencies.sort(Comparator.comparing(DependencyFact::from).thenComparing(DependencyFact::to).thenComparing(d -> d.location().file()).thenComparingInt(d -> d.location().startLine()));
        diagnostics.sort(Comparator.comparing((Diagnostic d) -> Objects.toString(d.file(), "")).thenComparing(Diagnostic::code).thenComparing(Diagnostic::message));
    }

    private void incomplete(String code, String message, String file) {
        semanticComplete = false;
        String key = code + ":" + file + ":" + message;
        if (reported.add(key)) diagnostics.add(new Diagnostic(code, message, file, "java"));
    }
}
