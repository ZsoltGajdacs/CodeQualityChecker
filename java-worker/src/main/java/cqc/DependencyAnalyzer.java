package cqc;

import com.github.javaparser.ast.body.TypeDeclaration;
import com.github.javaparser.ast.expr.MethodCallExpr;
import com.github.javaparser.ast.expr.FieldAccessExpr;
import com.github.javaparser.ast.expr.NameExpr;
import com.github.javaparser.ast.expr.Expression;
import com.github.javaparser.ast.expr.AnnotationExpr;
import com.github.javaparser.ast.expr.MethodReferenceExpr;
import com.github.javaparser.resolution.declarations.ResolvedValueDeclaration;
import com.github.javaparser.ast.type.ClassOrInterfaceType;
import java.util.*;
import static cqc.AstIdentity.*;

final class DependencyAnalyzer {
    private final MavenClasspath.Reporter reporter;
    private final Worker.Source source;
    private final Map<String, Worker.Location> edges = new TreeMap<>();
    private boolean complete = true;

    DependencyAnalyzer(MavenClasspath.Reporter reporter, Worker.Source source) { this.reporter = reporter; this.source = source; }
    record Result(List<Worker.DependencyFact> edges, boolean complete) { }

    Result analyze(TypeDeclaration<?> type) {
        String typeId = typeName(type);
        type.findAll(ClassOrInterfaceType.class).stream().filter(n -> owner(n).map(t -> t == type).orElse(false))
            .forEach(reference -> reference(reference));
        type.findAll(MethodCallExpr.class).stream().filter(n -> owner(n).map(t -> t == type).orElse(false))
            .forEach(call -> call(call));
        type.findAll(FieldAccessExpr.class).stream().filter(n -> owner(n).map(t -> t == type).orElse(false))
            .forEach(field -> field(field));
        type.findAll(NameExpr.class).stream().filter(n -> owner(n).map(t -> t == type).orElse(false))
            .forEach(name -> name(name));
        type.findAll(AnnotationExpr.class).stream().filter(n -> owner(n).map(t -> t == type).orElse(false))
            .forEach(annotation -> annotation(annotation));
        type.findAll(MethodReferenceExpr.class).stream().filter(n -> owner(n).map(t -> t == type).orElse(false))
            .forEach(reference -> methodReference(reference));
        var result = edges.entrySet().stream().filter(e -> !e.getKey().equals(typeId))
            .map(e -> new Worker.DependencyFact(typeId, e.getKey(), e.getValue())).toList();
        return new Result(result, complete);
    }

    private void reference(ClassOrInterfaceType reference) {
        try {
            var resolved = reference.resolve();
            if (resolved.isReferenceType()) edges.putIfAbsent(resolved.asReferenceType().getQualifiedName(), location(source.file(), reference));
        } catch (RuntimeException exception) {
            complete = false;
            reporter.report("java.unresolved_type", "Cannot resolve type " + reference.asString() + "; supply restored Maven classpath for complete dependency analysis.", source.file());
        }
    }

    private void call(MethodCallExpr call) {
        try { edges.putIfAbsent(call.resolve().declaringType().getQualifiedName(), location(source.file(), call)); }
        catch (RuntimeException exception) {
            complete = false;
            reporter.report("java.unresolved_call", "Cannot resolve call " + call.getNameAsString() + " at line " + line(call) + ".", source.file());
        }
    }

    private void field(FieldAccessExpr field) {
        try { value(field, field.resolve()); }
        catch (RuntimeException exception) { if (!qualifier(field)) unresolvedField(field.toString()); }
    }

    private void name(NameExpr name) {
        try { value(name, name.resolve()); }
        catch (RuntimeException exception) {
            // JavaParser represents a type qualifier (System in System.out) as a NameExpr.
            // Its enclosing field/method resolution supplies the actual dependency.
            if (!qualifier(name)) unresolvedField(name.toString());
        }
    }

    private static boolean qualifier(Expression expression) {
        var parent = expression.getParentNode().orElse(null);
        if (parent instanceof FieldAccessExpr field) return field.getScope() == expression;
        if (parent instanceof MethodCallExpr call) return call.getScope().map(scope -> scope == expression).orElse(false);
        return false;
    }

    private void annotation(AnnotationExpr annotation) {
        try { edges.putIfAbsent(annotation.resolve().getQualifiedName(), location(source.file(), annotation)); }
        catch (RuntimeException exception) { unresolvedField(annotation.toString()); }
    }

    private void methodReference(MethodReferenceExpr reference) {
        try { edges.putIfAbsent(reference.resolve().declaringType().getQualifiedName(), location(source.file(), reference)); }
        catch (RuntimeException exception) { unresolvedField(reference.toString()); }
    }

    private void value(Expression expression, ResolvedValueDeclaration value) {
        if (value.isField()) edges.putIfAbsent(value.asField().declaringType().getQualifiedName(), location(source.file(), expression));
        if (value.isEnumConstant()) edges.putIfAbsent(value.getType().asReferenceType().getQualifiedName(), location(source.file(), expression));
    }

    private void unresolvedField(String name) {
        complete = false;
        reporter.report("java.unresolved_field", "Cannot resolve field/name " + name + ".", source.file());
    }
}
