package cqc;

import com.github.javaparser.ast.body.*;
import java.util.*;
import static cqc.AstIdentity.*;

/** Extracts a single authored type; protocol handling and parser setup remain separate. */
final class TypeAnalysis {
    private final MavenClasspath.Reporter reporter;
    private final SymbolContexts symbols;
    private final MethodAnalyzer methods;
    private final boolean sourceOnly;
    record Result(Worker.TypeFact type, List<Worker.MethodFact> methods, List<Worker.DependencyFact> dependencies, List<Worker.ExecutableFact> executables) { }
    record Edges(List<Worker.DependencyFact> facts, boolean complete) { }
    private static final Edges NO_EDGES = new Edges(List.of(), false);

    TypeAnalysis(MavenClasspath.Reporter reporter, SymbolContexts symbols, boolean sourceOnly) {
        this.reporter = reporter;
        this.symbols = symbols;
        this.sourceOnly = sourceOnly;
        methods = new MethodAnalyzer(reporter, symbols, sourceOnly);
    }

    Result analyze(Worker.Source source, TypeDeclaration<?> type) {
        String name = typeName(type);
        String id = SymbolContexts.qualify(source.context().id(), name);
        boolean nested = ExecutableDiscovery.belongsToInitializer(type);
        if (sourceOnly || nested) id += sourceKey(source.file(), type);
        var edges = sourceOnly ? NO_EDGES : readEdges(source, type, id);
        var owner = new MethodAnalyzer.Owner(source, id);
        var executables = new ArrayList<Worker.ExecutableFact>();
        var discovery = new ExecutableDiscovery(unit -> executables.add(executable(owner, unit)));
        discovery.discover(type, nested);
        var fact = typeFact(owner, type, edges.complete() && !nested);
        var callableFacts = nested ? List.<Worker.MethodFact>of() : methods(owner, type, discovery);
        return new Result(fact, callableFacts, edges.facts(), executables);
    }

    private Worker.TypeFact typeFact(MethodAnalyzer.Owner owner, TypeDeclaration<?> type, boolean dependencies) {
        var source = owner.source();
        var context = source.context();
        String name = typeName(type);
        var evidence = new Worker.EvidenceState(true, true, true, dependencies, context.verified(), context.blockers());
        boolean isTest = context.sourceSet().equals("test") || type.getMethods().stream().anyMatch(TestRecognition::testAnnotation);
        boolean isAbstract = abstractType(type);
        return new Worker.TypeFact(owner.id(), "java", location(source.file(), type), isAbstract, isTest,
            dependencies && context.verified(), context.id(), name, evidence);
    }

    private static boolean abstractType(TypeDeclaration<?> type) {
        if (!(type instanceof ClassOrInterfaceDeclaration declaration)) return false;
        return declaration.isInterface() || declaration.isAbstract();
    }

    private List<Worker.MethodFact> methods(MethodAnalyzer.Owner owner, TypeDeclaration<?> type, ExecutableDiscovery discovery) {
        var facts = new ArrayList<Worker.MethodFact>();
        for (var member : type.getMembers()) {
            if (member instanceof MethodDeclaration method)
                method.getBody().ifPresent(body -> facts.add(methods.analyze(owner, method, discovery.measureCallable(body))));
            if (member instanceof ConstructorDeclaration constructor)
                facts.add(methods.analyze(owner, constructor, discovery.measureCallable(constructor.getBody())));
        }
        if (type instanceof RecordDeclaration record) record.getMembers().stream()
            .filter(CompactConstructorDeclaration.class::isInstance)
            .map(CompactConstructorDeclaration.class::cast)
            .map(compact -> methods.analyzeCompact(owner, record, compact, discovery.measure(compact.getBody())))
            .forEach(facts::add);
        return facts;
    }

    private Edges readEdges(Worker.Source source, TypeDeclaration<?> type, String id) {
        var dependencies = new DependencyAnalyzer(reporter, source).analyze(type);
        var edges = canonicalizeEdges(source, id, dependencies.edges());
        return new Edges(edges.facts(), dependencies.complete() && edges.complete());
    }

    private Edges canonicalizeEdges(Worker.Source source, String id, List<Worker.DependencyFact> edges) {
        var facts = new ArrayList<Worker.DependencyFact>();
        boolean complete = true;
        for (var edge : edges) {
            try { facts.add(new Worker.DependencyFact(id, symbols.type(edge.to(), source.context().id()), edge.location())); }
            catch (IllegalArgumentException exception) {
                complete = false;
                reporter.report("java.ambiguous_symbol", exception.getMessage(), source.file());
            }
        }
        return new Edges(facts, complete);
    }

    private static Worker.ExecutableFact executable(MethodAnalyzer.Owner owner, ExecutableDiscovery.Unit unit) {
        String file = owner.source().file();
        var body = unit.body();
        String id = owner.id() + "." + unit.kind() + sourceKey(file, body.root());
        var metrics = MethodMeasurement.inspect(body.nodes());
        return new Worker.ExecutableFact(id, owner.id(), "java", location(file, body.root()), metrics.complexity(),
            StructuralFingerprint.createOwned(body.nodes()), metrics.nodes(), unit.kind(), DuplicationLines.count(body));
    }
}
