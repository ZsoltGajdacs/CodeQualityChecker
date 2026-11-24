package cqc;

import com.github.javaparser.Range;
import com.github.javaparser.ast.Node;
import com.github.javaparser.ast.body.*;
import com.github.javaparser.ast.comments.Comment;
import com.github.javaparser.ast.expr.*;
import java.util.ArrayList;
import java.util.List;
import java.util.function.Consumer;

/** Finds initializer bodies and assigns nested executable nodes to their own source locations. */
final class ExecutableDiscovery {
    record Unit(String kind, Body body) { }
    record Body(Node root, List<Node> nodes, List<Range> excludedRanges) {
        private void exclude(Node node) { node.getRange().ifPresent(excludedRanges::add); }
    }

    private final Consumer<Unit> emit;

    ExecutableDiscovery(Consumer<Unit> emit) { this.emit = emit; }

    void discover(TypeDeclaration<?> type, boolean nested) {
        type.getMembers().forEach(nested ? this::nestedMember : this::initializer);
        if (type instanceof EnumDeclaration declaration) declaration.getEntries().forEach(this::enumConstant);
    }

    Body measure(Node body) { return measure(body, true); }

    Body measureCallable(Node body) { return measure(body, false); }

    private Body measure(Node root, boolean separateLambdas) {
        var body = new Body(root, new ArrayList<>(), new ArrayList<>());
        visit(root, body, separateLambdas);
        return body;
    }

    private void initializer(BodyDeclaration<?> member) {
        if (member instanceof FieldDeclaration field)
            field.getVariables().forEach(variable -> variable.getInitializer().ifPresent(value -> add("fieldInitializer", value)));
        if (member instanceof InitializerDeclaration block) add(block.isStatic() ? "staticInitializer" : "instanceInitializer", block.getBody());
    }

    private void nestedMember(BodyDeclaration<?> member) {
        initializer(member);
        if (member instanceof MethodDeclaration method) method.getBody().ifPresent(body -> add("nestedMethod", body));
        if (member instanceof ConstructorDeclaration constructor) add("nestedConstructor", constructor.getBody());
        if (member instanceof CompactConstructorDeclaration constructor) add("nestedConstructor", constructor.getBody());
    }

    private void enumConstant(EnumConstantDeclaration constant) {
        constant.getArguments().forEach(argument -> add("enumArgument", argument));
        constant.getClassBody().forEach(this::nestedMember);
    }

    private void add(String kind, Node body) { emit.accept(new Unit(kind, measure(body))); }

    private void visit(Node node, Body body, boolean separateLambdas) {
        if (node instanceof Comment) return;
        if (node instanceof TypeDeclaration<?>) {
            body.exclude(node);
            return;
        }
        body.nodes().add(node);
        if (separateLambdas && node instanceof LambdaExpr lambda) {
            body.exclude(lambda.getBody());
            add("lambda", lambda.getBody());
            return;
        }
        node.getChildNodes().forEach(child -> visitChild(child, body, separateLambdas));
    }

    private void visitChild(Node child, Body body, boolean separateLambdas) {
        if (child instanceof BodyDeclaration<?> member) {
            body.exclude(member);
            nestedMember(member);
        } else visit(child, body, separateLambdas);
    }

    static boolean belongsToInitializer(TypeDeclaration<?> type) {
        for (var node = type.getParentNode(); node.isPresent(); node = node.get().getParentNode()) {
            if (initializerOwner(node.get())) return true;
        }
        return false;
    }

    private static boolean initializerOwner(Node node) {
        return node instanceof FieldDeclaration || node instanceof InitializerDeclaration
            || node instanceof EnumConstantDeclaration || node instanceof CompactConstructorDeclaration;
    }
}
