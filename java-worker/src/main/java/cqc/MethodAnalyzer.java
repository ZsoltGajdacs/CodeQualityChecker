package cqc;

import com.github.javaparser.ast.body.*;
import com.github.javaparser.ast.stmt.*;
import java.util.*;
import static cqc.AstIdentity.*;

/** Joins independent syntax and resolution facts without letting resolution erase measurements. */
final class MethodAnalyzer {
    private static final Set<String> FIXTURES = Set.of("BeforeEach", "AfterEach", "BeforeAll", "AfterAll");
    private final MethodResolution resolution;
    private final boolean sourceOnly;
    record Owner(Worker.Source source, String id, boolean syntax) { }
    private record ResolvedMethod(String id, MethodResolution.Identity identity, MethodResolution.Calls calls, String signature) { }

    MethodAnalyzer(MavenClasspath.Reporter reporter, SymbolContexts symbols, boolean sourceOnly) {
        this.sourceOnly = sourceOnly;
        resolution = new MethodResolution(reporter, symbols);
    }

    Worker.MethodFact analyze(Owner owner, CallableDeclaration<?> declaration, BlockStmt body) {
        var source = owner.source();
        var method = methodIdentity(source, declaration, body);
        var identity = method.identity();
        var calls = method.calls();
        var nodes = MethodMeasurement.nodes(declaration, body);
        var metrics = MethodMeasurement.inspect(nodes);
        var fingerprints = StructuralFingerprint.create(declaration, nodes);
        var context = source.context();
        var evidence = new Worker.EvidenceState(owner.syntax(), identity.complete(), calls.complete(), !sourceOnly, context.verified(), context.blockers());
        boolean parameterized = TestRecognition.annotation(declaration, "ParameterizedTest");
        boolean fixture = FIXTURES.stream().anyMatch(name -> TestRecognition.annotation(declaration, name));
        return new Worker.MethodFact(method.id(), owner.id(),
            "java", location(source.file(), declaration), metrics.complexity(), metrics.lines(), TestRecognition.testAnnotation(declaration), fixture, parameterized,
            calls.assertions(), calls.ids(), fingerprints, metrics.nodes(), owner.syntax() && identity.complete() && calls.complete() && context.verified(),
            method.signature(), context.id(), identity.name(), evidence);
    }

    private ResolvedMethod methodIdentity(Worker.Source source, CallableDeclaration<?> declaration, BlockStmt body) {
        String typeName = owner(declaration).map(AstIdentity::typeName).orElseThrow();
        if (sourceOnly) {
            String name = MethodResolution.sourceSignature(typeName, declaration);
            String id = SymbolContexts.qualify(source.context().id(), name) + sourceKey(source.file(), declaration);
            return new ResolvedMethod(id, new MethodResolution.Identity(name, false), MethodResolution.Calls.NOT_ATTEMPTED, null);
        }
        var identity = resolution.identity(source.file(), typeName, declaration);
        String id = SymbolContexts.qualify(source.context().id(), identity.name());
        return new ResolvedMethod(id, identity, resolution.calls(source, declaration, body), MethodResolution.coverageSignature(declaration));
    }
}
