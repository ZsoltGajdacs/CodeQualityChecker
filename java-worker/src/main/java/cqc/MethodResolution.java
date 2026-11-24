package cqc;

import com.github.javaparser.ast.Node;
import com.github.javaparser.ast.body.*;
import com.github.javaparser.ast.expr.*;
import com.github.javaparser.ast.stmt.*;
import java.util.*;
import java.util.function.Function;
import java.util.stream.Collectors;
import java.util.stream.Stream;
import static cqc.AstIdentity.*;

/** Semantic method facts are independent of syntax-based measurements. */
final class MethodResolution {
    record Identity(String name, boolean complete) { }
    record Calls(String[] ids, int assertions, boolean complete) {
        static final Calls NOT_ATTEMPTED = new Calls(new String[0], 0, false);
    }
    private final MavenClasspath.Reporter reporter;
    private final SymbolContexts symbols;

    MethodResolution(MavenClasspath.Reporter reporter, SymbolContexts symbols) { this.reporter = reporter; this.symbols = symbols; }

    Identity identity(String file, String typeId, CallableDeclaration<?> declaration) {
        try { return new Identity(callableId(declaration), true); }
        catch (RuntimeException exception) {
            String id = sourceSignature(typeId, declaration);
            reporter.report("java.unresolved_signature", "Cannot resolve signature " + id + ".", file);
            return new Identity(id, false);
        }
    }

    static String sourceSignature(String typeName, CallableDeclaration<?> declaration) {
        String parameters = declaration.getParameters().stream().map(p -> p.getType().asString() + (p.isVarArgs() ? "[]" : "")).collect(Collectors.joining(","));
        String name = declaration instanceof ConstructorDeclaration ? ".ctor" : declaration.getNameAsString();
        return typeName + "." + name + "(" + parameters + ")";
    }

    static String coverageSignature(CallableDeclaration<?> declaration) {
        try {
            String parameters = declaration.getParameters().stream().map(p -> p.getType().resolve().erasure().describe() + (p.isVarArgs() ? "[]" : "")).collect(Collectors.joining(","));
            String name = declaration instanceof ConstructorDeclaration ? ".ctor" : declaration.getNameAsString();
            return owner(declaration).map(AstIdentity::typeName).orElseThrow() + "." + name + "(" + parameters + ")";
        } catch (RuntimeException exception) { return null; }
    }

    Identity compactIdentity(Worker.Source source, RecordDeclaration record, boolean sourceOnly) {
        if (!sourceOnly) {
            try { return new Identity(compactSignature(record, parameter -> parameter.getType().resolve().describe()), true); }
            catch (RuntimeException exception) {
                reporter.report("java.unresolved_signature", "Cannot resolve compact constructor signature for " + typeName(record) + ".", source.file());
            }
        }
        return new Identity(compactSignature(record, parameter -> parameter.getType().asString()), false);
    }

    static String compactCoverageSignature(RecordDeclaration record) {
        try { return compactSignature(record, parameter -> parameter.getType().resolve().erasure().describe()); }
        catch (RuntimeException exception) { return null; }
    }

    private static String compactSignature(RecordDeclaration record, Function<Parameter, String> parameterType) {
        String parameters = record.getParameters().stream()
            .map(parameter -> parameterType.apply(parameter) + (parameter.isVarArgs() ? "[]" : ""))
            .collect(Collectors.joining(","));
        return typeName(record) + "..ctor(" + parameters + ")";
    }

    Calls calls(Worker.Source source, Stream<Node> nodes) {
        var accumulator = new CallAccumulator(source);
        nodes.forEach(accumulator::accept);
        return accumulator.result();
    }

    private final class CallAccumulator {
        private final Worker.Source source;
        private final Set<String> ids = new TreeSet<>();
        private int assertions;
        private boolean complete = true;

        private CallAccumulator(Worker.Source source) { this.source = source; }

        private void accept(Node node) {
            try {
                if (node instanceof AssertStmt) assertions++;
                if (node instanceof MethodCallExpr call) {
                    var resolved = call.resolve();
                    add(resolved);
                    if (TestRecognition.assertion(resolved)) assertions++;
                }
                if (node instanceof MethodReferenceExpr reference) add(reference.resolve());
            } catch (RuntimeException exception) { complete = false; }
        }

        private void add(com.github.javaparser.resolution.declarations.ResolvedMethodDeclaration method) {
            ids.add(symbols.method(methodId(method), method.declaringType().getQualifiedName(), source.context().id()));
        }

        private Calls result() { return new Calls(ids.toArray(String[]::new), assertions, complete); }
    }
}
