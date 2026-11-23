package cqc;

import com.github.javaparser.ast.body.*;
import com.github.javaparser.ast.expr.*;
import java.util.*;
import static cqc.AstIdentity.*;

/** Extracts a single authored type; protocol handling and parser setup remain separate. */
final class TypeAnalysis {
    private static final Set<Class<?>> UNMODELED_EXECUTABLE = Set.of(CompactConstructorDeclaration.class, InitializerDeclaration.class);
    private final MavenClasspath.Reporter reporter;
    private final SymbolContexts symbols;
    private final MethodAnalyzer methods;
    private final boolean sourceOnly;
    record Result(Worker.TypeFact type, List<Worker.MethodFact> methods, List<Worker.DependencyFact> dependencies, List<Worker.SyntaxGap> gaps) { }
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
        if (sourceOnly) id += sourceKey(source.file(), type);
        var edges = sourceOnly ? NO_EDGES : readEdges(source, type, id);
        var gaps = syntaxGaps(source, type);
        var owner = new MethodAnalyzer.Owner(source, id, gaps.isEmpty());
        var fact = typeFact(owner, type, edges.complete());
        return new Result(fact, methods(owner, type), edges.facts(), gaps);
    }

    private Worker.TypeFact typeFact(MethodAnalyzer.Owner owner, TypeDeclaration<?> type, boolean dependencies) {
        var source = owner.source();
        var context = source.context();
        String name = typeName(type);
        var evidence = new Worker.EvidenceState(owner.syntax(), true, true, dependencies, context.verified(), context.blockers());
        boolean isTest = context.sourceSet().equals("test") || type.getMethods().stream().anyMatch(TestRecognition::testAnnotation);
        boolean isAbstract = abstractType(type);
        return new Worker.TypeFact(owner.id(), "java", location(source.file(), type), isAbstract, isTest,
            owner.syntax() && dependencies && context.verified(), context.id(), name, evidence);
    }

    private static boolean abstractType(TypeDeclaration<?> type) {
        if (!(type instanceof ClassOrInterfaceDeclaration declaration)) return false;
        return declaration.isInterface() || declaration.isAbstract();
    }

    private List<Worker.MethodFact> methods(MethodAnalyzer.Owner owner, TypeDeclaration<?> type) {
        var facts = new ArrayList<Worker.MethodFact>();
        for (var member : type.getMembers()) {
            if (member instanceof MethodDeclaration method)
                method.getBody().ifPresent(body -> facts.add(methods.analyze(owner, method, body)));
            if (member instanceof ConstructorDeclaration constructor)
                facts.add(methods.analyze(owner, constructor, constructor.getBody()));
        }
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

    private static boolean unmodeledMembers(TypeDeclaration<?> type) {
        if (type.getMembers().stream().anyMatch(member -> UNMODELED_EXECUTABLE.contains(member.getClass()))) return true;
        return type.getFields().stream().flatMap(field -> field.getVariables().stream())
            .flatMap(variable -> variable.getInitializer().stream()).anyMatch(initializer -> !constantInitializer(initializer));
    }

    private static boolean unmodeledEnumConstants(TypeDeclaration<?> type) {
        if (!(type instanceof EnumDeclaration declaration)) return false;
        return declaration.getEntries().stream().anyMatch(entry -> !entry.getClassBody().isEmpty()
            || entry.getArguments().stream().anyMatch(argument -> !constantInitializer(argument)));
    }

    private static boolean constantInitializer(Expression expression) {
        if (expression instanceof LiteralExpr) return true;
        if (expression instanceof ClassExpr) return true;
        if (expression instanceof EnclosedExpr enclosed) return constantInitializer(enclosed.getInner());
        if (expression instanceof UnaryExpr unary) return constantInitializer(unary.getExpression());
        if (expression instanceof BinaryExpr binary)
            return constantInitializer(binary.getLeft()) && constantInitializer(binary.getRight());
        return false;
    }

    private List<Worker.SyntaxGap> syntaxGaps(Worker.Source source, TypeDeclaration<?> type) {
        if (!unmodeledMembers(type) && !unmodeledEnumConstants(type)) return List.of();
        reporter.report("java.unmodeled_executable", "Executable initializers, enum constant bodies and compact constructors are not modeled yet; syntax scoring for " + typeName(type) + " is unavailable.", source.file());
        return List.of(new Worker.SyntaxGap(source.file(), "java", source.context().id(), "syntax.unmodeledExecutable", false));
    }
}
