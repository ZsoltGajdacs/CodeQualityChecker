package cqc;

import com.github.javaparser.ast.Node;
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
    private final Map<String, Set<Worker.Location>> references = new TreeMap<>();
    private boolean complete = true;

    DependencyAnalyzer(MavenClasspath.Reporter reporter, Worker.Source source) { this.reporter = reporter; this.source = source; }
    record Result(List<Worker.DependencyFact> edges, boolean complete) { }

    Result analyze(TypeDeclaration<?> type) {
        String typeId = typeName(type);
        type.findAll(ClassOrInterfaceType.class).stream().filter(n -> owner(n).map(t -> t == type).orElse(false))
            .filter(reference -> !typeQualifier(reference)).forEach(this::reference);
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
        var result = references.entrySet().stream().filter(e -> !e.getKey().equals(typeId))
            .flatMap(entry -> entry.getValue().stream()
                .map(location -> new Worker.DependencyFact(typeId, entry.getKey(), location))).toList();
        return new Result(result, complete);
    }

    private static boolean typeQualifier(ClassOrInterfaceType reference) {
        return reference.getParentNode().orElse(null) instanceof ClassOrInterfaceType qualified
            && qualified.getScope().orElse(null) == reference;
    }

    private void reference(ClassOrInterfaceType reference) {
        try {
            var resolved = reference.resolve();
            if (resolved.isReferenceType()) add(resolved.asReferenceType().getQualifiedName(), reference);
        } catch (RuntimeException exception) {
            unresolved("type", display(reference), reference);
        }
    }

    private void call(MethodCallExpr call) {
        try { add(call.resolve().declaringType().getQualifiedName(), call); }
        catch (RuntimeException exception) {
            String target = call.getScope().map(scope -> display(scope) + ".").orElse("") + call.getNameAsString();
            unresolved("call", target, call);
        }
    }

    private void field(FieldAccessExpr field) {
        try { value(field, field.resolve()); }
        catch (RuntimeException exception) { if (!qualifier(field)) unresolved("field", display(field), field); }
    }

    private void name(NameExpr name) {
        try { value(name, name.resolve()); }
        catch (RuntimeException exception) {
            // JavaParser represents a type qualifier (System in System.out) as a NameExpr.
            // Its enclosing field/method resolution supplies the actual dependency.
            if (!qualifier(name)) unresolved("field", name.getNameAsString(), name);
        }
    }

    private static boolean qualifier(Expression expression) {
        var parent = expression.getParentNode().orElse(null);
        if (parent instanceof FieldAccessExpr field) return field.getScope() == expression;
        if (parent instanceof MethodCallExpr call) return call.getScope().orElse(null) == expression;
        if (parent instanceof MethodReferenceExpr reference) return reference.getScope() == expression;
        return false;
    }

    private void annotation(AnnotationExpr annotation) {
        try { add(annotation.resolve().getQualifiedName(), annotation); }
        catch (RuntimeException exception) { unresolved("annotation", annotation.getNameAsString(), annotation); }
    }

    private void methodReference(MethodReferenceExpr reference) {
        try { add(reference.resolve().declaringType().getQualifiedName(), reference); }
        catch (RuntimeException exception) {
            unresolved("method_reference", display(reference.getScope()) + "::" + reference.getIdentifier(), reference);
        }
    }

    private void value(Expression expression, ResolvedValueDeclaration value) {
        if (value.isField()) add(value.asField().declaringType().getQualifiedName(), expression);
        if (value.isEnumConstant()) add(value.getType().asReferenceType().getQualifiedName(), expression);
    }

    private void add(String target, Node reference) {
        references.computeIfAbsent(target, ignored -> new LinkedHashSet<>()).add(location(source.file(), reference));
    }

    private void unresolved(String kind, String name, Node node) {
        complete = false;
        String description = kind.replace('_', ' ');
        reporter.report("java.unresolved_" + kind, "Cannot resolve " + description + " " + name + ".",
            location(source.file(), node), name);
    }

    private static String display(Node node) {
        var tokens = node.getTokenRange().orElse(null);
        if (tokens == null) return node.getClass().getSimpleName();
        var text = new StringBuilder();
        for (var token : tokens) {
            String piece = token.getText();
            int available = 100 - text.length();
            text.append(piece, 0, Math.min(available, piece.length()));
            if (piece.length() > available) return text.substring(0, 97).replaceAll("\\s+", " ") + "...";
        }
        return text.toString().replaceAll("\\s+", " ");
    }
}
