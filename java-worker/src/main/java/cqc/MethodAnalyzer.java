package cqc;

import com.github.javaparser.ast.body.*;
import com.github.javaparser.ast.stmt.*;
import java.util.*;
import static cqc.AstIdentity.*;

/** Joins independent syntax and resolution facts without letting resolution erase measurements. */
final class MethodAnalyzer {
    private static final Set<String> FIXTURES = Set.of("BeforeEach", "AfterEach", "BeforeAll", "AfterAll");
    private final MethodResolution resolution;

    MethodAnalyzer(MavenClasspath.Reporter reporter, SymbolContexts symbols) { resolution = new MethodResolution(reporter, symbols); }

    Worker.MethodFact analyze(Worker.Source source, CallableDeclaration<?> declaration, BlockStmt body, boolean syntax) {
        String typeName = owner(declaration).map(AstIdentity::typeName).orElseThrow();
        var identity = resolution.identity(source.file(), typeName, declaration);
        var calls = resolution.calls(source, declaration, body);
        var nodes = MethodMeasurement.nodes(declaration, body);
        var metrics = MethodMeasurement.inspect(nodes);
        var fingerprints = StructuralFingerprint.create(declaration, nodes);
        var context = source.context();
        var evidence = new Worker.EvidenceState(syntax, identity.complete(), calls.complete(), true, context.verified(), context.blockers());
        boolean parameterized = TestRecognition.annotation(declaration, "ParameterizedTest");
        boolean fixture = FIXTURES.stream().anyMatch(name -> TestRecognition.annotation(declaration, name));
        return new Worker.MethodFact(SymbolContexts.qualify(context.id(), identity.name()), SymbolContexts.qualify(context.id(), typeName),
            "java", location(source.file(), declaration), metrics.complexity(), metrics.lines(), TestRecognition.testAnnotation(declaration), fixture, parameterized,
            calls.assertions(), calls.ids(), fingerprints, metrics.nodes(), syntax && identity.complete() && calls.complete() && context.verified(),
            MethodResolution.coverageSignature(declaration), context.id(), identity.name(), evidence);
    }
}
