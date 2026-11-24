package cqc;

import com.github.javaparser.ast.Node;
import com.github.javaparser.ast.body.*;
import java.util.*;
import static cqc.AstIdentity.*;

/** Joins independent syntax and resolution facts without letting resolution erase measurements. */
final class MethodAnalyzer {
    private static final Set<String> FIXTURES = Set.of("BeforeEach", "AfterEach", "BeforeAll", "AfterAll");
    private final MethodResolution resolution;
    private final boolean sourceOnly;
    record Owner(Worker.Source source, String id) { }
    private record ResolvedMethod(String id, MethodResolution.Identity identity, MethodResolution.Calls calls, String signature) { }
    private record CallableSource(Node declaration, MethodRoles roles) { }
    private record Measurement(MethodMeasurement.Metrics metrics, String[] fingerprints) {
        static Measurement of(List<Node> nodes, String[] fingerprints) {
            return new Measurement(MethodMeasurement.inspect(nodes), fingerprints);
        }
    }

    MethodAnalyzer(MavenClasspath.Reporter reporter, SymbolContexts symbols, boolean sourceOnly) {
        this.sourceOnly = sourceOnly;
        resolution = new MethodResolution(reporter, symbols);
    }

    Worker.MethodFact analyze(Owner owner, CallableDeclaration<?> declaration, List<Node> nodes) {
        var method = methodIdentity(owner.source(), declaration, nodes);
        var fingerprints = StructuralFingerprint.create(declaration, nodes);
        return methodFact(owner, new CallableSource(declaration, roles(declaration)), method, Measurement.of(nodes, fingerprints));
    }

    Worker.MethodFact analyzeCompact(Owner owner, RecordDeclaration record, CompactConstructorDeclaration constructor, List<Node> nodes) {
        var source = owner.source();
        var identity = resolution.compactIdentity(source, record, sourceOnly);
        var calls = sourceOnly ? MethodResolution.Calls.NOT_ATTEMPTED : resolution.calls(source, nodes.stream());
        String signature = sourceOnly ? null : MethodResolution.compactCoverageSignature(record);
        String id = SymbolContexts.qualify(source.context().id(), identity.name());
        if (sourceOnly) id += sourceKey(source.file(), constructor);
        var method = new ResolvedMethod(id, identity, calls, signature);
        var measurement = Measurement.of(nodes, StructuralFingerprint.createOwned(nodes));
        return methodFact(owner, new CallableSource(constructor, MethodRoles.CONSTRUCTOR), method, measurement);
    }

    private record MethodRoles(boolean test, boolean fixture, boolean parameterized) {
        static final MethodRoles CONSTRUCTOR = new MethodRoles(false, false, false);
    }

    private static MethodRoles roles(CallableDeclaration<?> declaration) {
        return new MethodRoles(TestRecognition.testAnnotation(declaration),
            FIXTURES.stream().anyMatch(name -> TestRecognition.annotation(declaration, name)),
            TestRecognition.annotation(declaration, "ParameterizedTest"));
    }

    private Worker.MethodFact methodFact(Owner owner, CallableSource callable, ResolvedMethod method, Measurement measurement) {
        var source = owner.source();
        var metrics = measurement.metrics();
        var roles = callable.roles();
        var context = source.context();
        var identity = method.identity();
        var calls = method.calls();
        var evidence = new Worker.EvidenceState(true, identity.complete(), calls.complete(), !sourceOnly, context.verified(), context.blockers());
        return new Worker.MethodFact(method.id(), owner.id(), "java", location(source.file(), callable.declaration()), metrics.complexity(),
            metrics.lines(), roles.test(), roles.fixture(), roles.parameterized(), calls.assertions(), calls.ids(), measurement.fingerprints(),
            metrics.nodes(), identity.complete() && calls.complete() && context.verified(), method.signature(), context.id(), identity.name(), evidence);
    }

    private ResolvedMethod methodIdentity(Worker.Source source, CallableDeclaration<?> declaration, List<Node> nodes) {
        String typeName = owner(declaration).map(AstIdentity::typeName).orElseThrow();
        if (sourceOnly) {
            String name = MethodResolution.sourceSignature(typeName, declaration);
            String id = SymbolContexts.qualify(source.context().id(), name) + sourceKey(source.file(), declaration);
            return new ResolvedMethod(id, new MethodResolution.Identity(name, false), MethodResolution.Calls.NOT_ATTEMPTED, null);
        }
        var identity = resolution.identity(source.file(), typeName, declaration);
        String id = SymbolContexts.qualify(source.context().id(), identity.name());
        return new ResolvedMethod(id, identity, resolution.calls(source, nodes.stream()), MethodResolution.coverageSignature(declaration));
    }
}
