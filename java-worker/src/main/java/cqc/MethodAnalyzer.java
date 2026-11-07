package cqc;

import com.github.javaparser.ast.body.*;
import com.github.javaparser.ast.expr.MethodCallExpr;
import com.github.javaparser.ast.expr.MethodReferenceExpr;
import com.github.javaparser.ast.stmt.*;
import java.util.*;
import java.util.stream.Collectors;
import static cqc.AstIdentity.*;

final class MethodAnalyzer {
    private static final Set<String> FIXTURES = Set.of("BeforeEach", "AfterEach", "BeforeAll", "AfterAll");
    private final MavenClasspath.Reporter reporter;
    private final boolean ambiguousClasspath;
    record Identity(String id, boolean complete) { }
    record Calls(String[] ids, int assertions, boolean complete) { }

    MethodAnalyzer(MavenClasspath.Reporter reporter, boolean ambiguousClasspath) {
        this.reporter = reporter;
        this.ambiguousClasspath = ambiguousClasspath;
    }

    Worker.MethodFact analyze(Worker.Source source, String typeId, CallableDeclaration<?> declaration, BlockStmt body) {
        var identity = identity(source.file(), typeId, declaration);
        var calls = calls(declaration, body);
        var metrics = MethodMeasurement.inspect(declaration, body);
        boolean parameterized = TestRecognition.annotation(declaration, "ParameterizedTest");
        boolean fixture = FIXTURES.stream().anyMatch(name -> TestRecognition.annotation(declaration, name));
        boolean complete = identity.complete() && calls.complete() && !ambiguousClasspath;
        return new Worker.MethodFact(identity.id(), typeId, "java", location(source.file(), declaration), metrics.complexity(),
            metrics.lines(), TestRecognition.testAnnotation(declaration), fixture, parameterized,
            calls.assertions(), calls.ids(), metrics.fingerprints(), metrics.nodes(), complete, coverageSignature(declaration));
    }

    private static String coverageSignature(CallableDeclaration<?> declaration) {
        try {
            String parameters = declaration.getParameters().stream().map(p -> p.getType().resolve().erasure().describe() + (p.isVarArgs() ? "[]" : "")).collect(Collectors.joining(","));
            String name = declaration instanceof ConstructorDeclaration ? ".ctor" : declaration.getNameAsString();
            return owner(declaration).map(AstIdentity::typeName).orElseThrow() + "." + name + "(" + parameters + ")";
        } catch (RuntimeException exception) { return null; }
    }

    private Identity identity(String file, String typeId, CallableDeclaration<?> declaration) {
        try { return new Identity(callableId(declaration), true); }
        catch (RuntimeException exception) {
            String parameters = declaration.getParameters().stream().map(p -> p.getType().asString() + (p.isVarArgs() ? "[]" : "")).collect(Collectors.joining(","));
            String name = declaration instanceof ConstructorDeclaration ? ".ctor" : declaration.getNameAsString();
            String id = typeId + "." + name + "(" + parameters + ")";
            reporter.report("java.unresolved_signature", "Cannot resolve signature " + id + ".", file);
            return new Identity(id, false);
        }
    }

    private static Calls calls(CallableDeclaration<?> declaration, BlockStmt body) {
        var ids = new TreeSet<String>();
        int assertions = (int)body.findAll(AssertStmt.class).stream().filter(n -> callable(n).map(c -> c == declaration).orElse(false)).count();
        boolean complete = true;
        var ownCalls = body.findAll(MethodCallExpr.class).stream().filter(n -> callable(n).map(c -> c == declaration).orElse(false));
        for (var call : ownCalls.toList()) {
            try {
                var resolved = call.resolve();
                ids.add(methodId(resolved));
                if (TestRecognition.assertion(resolved)) assertions++;
            } catch (RuntimeException exception) { complete = false; }
        }
        for (var reference : body.findAll(MethodReferenceExpr.class)) {
            try { ids.add(methodId(reference.resolve())); }
            catch (RuntimeException exception) { complete = false; }
        }
        return new Calls(ids.toArray(String[]::new), assertions, complete);
    }
}
