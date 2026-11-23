package cqc;

import com.github.javaparser.ast.body.*;
import com.github.javaparser.ast.expr.*;
import com.github.javaparser.ast.stmt.*;
import java.util.*;
import java.util.stream.Collectors;
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

    Calls calls(Worker.Source source, CallableDeclaration<?> declaration, BlockStmt body) {
        var ids = new TreeSet<String>();
        int assertions = (int)body.findAll(AssertStmt.class).stream().filter(n -> callable(n).map(c -> c == declaration).orElse(false)).count();
        boolean complete = true;
        var ownCalls = body.findAll(MethodCallExpr.class).stream().filter(n -> callable(n).map(c -> c == declaration).orElse(false));
        for (var call : ownCalls.toList()) {
            try {
                var resolved = call.resolve();
                ids.add(symbols.method(methodId(resolved), resolved.declaringType().getQualifiedName(), source.context().id()));
                if (TestRecognition.assertion(resolved)) assertions++;
            } catch (RuntimeException exception) { complete = false; }
        }
        for (var reference : body.findAll(MethodReferenceExpr.class).stream().filter(n -> callable(n).map(c -> c == declaration).orElse(false)).toList()) {
            try {
                var resolved = reference.resolve();
                ids.add(symbols.method(methodId(resolved), resolved.declaringType().getQualifiedName(), source.context().id()));
            } catch (RuntimeException exception) { complete = false; }
        }
        return new Calls(ids.toArray(String[]::new), assertions, complete);
    }
}
