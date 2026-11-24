package cqc;

import com.github.javaparser.ast.CompilationUnit;
import com.github.javaparser.ast.body.*;
import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import static cqc.AstIdentity.*;

/** Protocol orchestration and deterministic fact assembly; target build tools are never invoked. */
public final class Worker {
    private static final Gson JSON = new GsonBuilder().serializeNulls().create();
    private final List<TypeFact> types = new ArrayList<>();
    private final List<MethodFact> methods = new ArrayList<>();
    private final List<DependencyFact> dependencies = new ArrayList<>();
    private final List<Diagnostic> diagnostics = new ArrayList<>();
    private final Set<String> reported = new HashSet<>();
    private final List<SyntaxGap> syntaxGaps = new ArrayList<>();

    record Module(String language, String path) { }
    record Snapshot(String root, String[] files, String sourceFingerprint, Module[] modules, String schemaVersion, boolean sourceOnly,
        String referenceEvidenceDirectory) { }
    record Location(String file, int startLine, int endLine) { }
    record Diagnostic(String code, String message, String file, String language) { }
    record EvidenceState(boolean syntax, boolean identity, boolean calls, boolean dependencies, boolean verifiedContext, String[] blockers) {
        EvidenceState {
            var reasons = new TreeSet<>(Arrays.asList(blockers));
            if (!syntax) reasons.add("syntax.incomplete");
            if (!identity) reasons.add("identity.unresolved");
            if (!calls) reasons.add("tests.unresolvedCalls");
            if (!dependencies) reasons.add("dependencies.unresolved");
            if (!verifiedContext) reasons.add("context.unverified");
            blockers = reasons.toArray(String[]::new);
        }
    }
    record AnalysisContext(String id, String modulePath, String sourceSet, String[] reachableContexts, boolean verified, String[] blockers) { }
    record SyntaxGap(String file, String language, String contextId, String code, boolean unknownEntities) { }
    record TypeFact(String id, String language, Location location, boolean isAbstract, boolean isTest, boolean complete,
        String contextId, String qualifiedName, EvidenceState evidence) { }
    record DependencyFact(String from, String to, Location location) { }
    record MethodFact(String id, String typeId, String language, Location location, int complexity,
        int executableLines, boolean isTest, boolean isFixture, boolean isParameterized,
        int assertions, String[] calls, String[] fingerprints, int nodeCount, boolean complete, String coverageSignature,
        String contextId, String qualifiedName, EvidenceState evidence) { }
    record AnalysisFacts(List<TypeFact> types, List<MethodFact> methods, List<DependencyFact> dependencies,
        List<Diagnostic> diagnostics, boolean semanticComplete, String schemaVersion, List<AnalysisContext> contexts, List<SyntaxGap> syntaxGaps) { }
    record Source(String file, CompilationUnit unit, ContextManifest.Context context) { }

    public static void main(String[] args) {
        try {
            var input = JSON.fromJson(new InputStreamReader(System.in, StandardCharsets.UTF_8), Snapshot.class);
            Objects.requireNonNull(input, "Expected a snapshot object.");
            Objects.requireNonNull(input.root(), "Snapshot root is required.");
            Objects.requireNonNull(input.files(), "Snapshot files are required.");
            String version = input.sourceOnly() ? "3" : "2";
            if (!version.equals(input.schemaVersion())) throw new IllegalArgumentException("Unsupported snapshot schemaVersion; expected " + version + ". Install matching server and worker.");
            System.out.print(JSON.toJson(new Worker().analyze(input)));
        } catch (Exception exception) {
            System.err.println(exception.getClass().getSimpleName() + ": " + exception.getMessage());
            System.exit(1);
        }
    }

    private AnalysisFacts analyze(Snapshot input) throws IOException {
        var parsed = new SnapshotParser(input, this::diagnostic).load();
        var analyzer = new TypeAnalysis(this::diagnostic, parsed.symbols(), input.sourceOnly());
        syntaxGaps.addAll(parsed.gaps());
        for (var source : parsed.sources())
            for (var type : source.unit().findAll(TypeDeclaration.class)) {
                var result = analyzer.analyze(source, type);
                types.add(result.type());
                methods.addAll(result.methods());
                dependencies.addAll(result.dependencies());
                syntaxGaps.addAll(result.gaps());
            }
        sortResults();
        boolean complete = syntaxGaps.isEmpty() && types.stream().allMatch(TypeFact::complete) && methods.stream().allMatch(MethodFact::complete);
        return new AnalysisFacts(types, methods, dependencies, diagnostics, complete, input.schemaVersion(),
            parsed.contexts().stream().map(ContextManifest.Context::fact).sorted(Comparator.comparing(AnalysisContext::id)).toList(), syntaxGaps);
    }

    private void sortResults() {
        types.sort(Comparator.comparing(TypeFact::id).thenComparing(t -> t.location().file()).thenComparingInt(t -> t.location().startLine()));
        methods.sort(Comparator.comparing(MethodFact::id).thenComparing(m -> m.location().file()));
        dependencies.sort(Comparator.comparing(DependencyFact::from).thenComparing(DependencyFact::to).thenComparing(d -> d.location().file()).thenComparingInt(d -> d.location().startLine()));
        diagnostics.sort(Comparator.comparing((Diagnostic d) -> Objects.toString(d.file(), "")).thenComparing(Diagnostic::code).thenComparing(Diagnostic::message));
        syntaxGaps.sort(Comparator.comparing(SyntaxGap::file).thenComparing(SyntaxGap::code));
    }

    private void diagnostic(String code, String message, String file) {
        if (reported.add(code + ":" + file + ":" + message)) diagnostics.add(new Diagnostic(code, message, file, "java"));
    }
}
