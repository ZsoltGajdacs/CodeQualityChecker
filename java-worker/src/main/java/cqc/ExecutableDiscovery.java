package cqc;

import com.github.javaparser.ast.Node;
import com.github.javaparser.ast.body.*;
import com.github.javaparser.ast.comments.Comment;
import com.github.javaparser.ast.expr.*;
import java.util.ArrayList;
import java.util.List;
import java.util.function.Consumer;

/** Finds initializer bodies and assigns nested executable nodes to their own source locations. */
final class ExecutableDiscovery {
    record Unit(String kind, Node body, List<Node> nodes) { }

    private final Consumer<Unit> emit;

    ExecutableDiscovery(Consumer<Unit> emit) { this.emit = emit; }

    void discover(TypeDeclaration<?> type, boolean nested) {
        type.getMembers().forEach(nested ? this::nestedMember : this::initializer);
        if (type instanceof EnumDeclaration declaration) declaration.getEntries().forEach(this::enumConstant);
    }

    List<Node> measure(Node body) { return measure(body, true); }

    List<Node> measureCallable(Node body) { return measure(body, false); }

    private List<Node> measure(Node body, boolean separateLambdas) {
        var owned = new ArrayList<Node>();
        visit(body, owned, separateLambdas);
        return owned;
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

    private void add(String kind, Node body) { emit.accept(new Unit(kind, body, measure(body))); }

    private void visit(Node node, List<Node> owned, boolean separateLambdas) {
        if (node instanceof Comment || node instanceof TypeDeclaration<?>) return;
        owned.add(node);
        if (separateLambdas && node instanceof LambdaExpr lambda) {
            add("lambda", lambda.getBody());
            return;
        }
        node.getChildNodes().forEach(child -> visitChild(child, owned, separateLambdas));
    }

    private void visitChild(Node child, List<Node> owned, boolean separateLambdas) {
        if (child instanceof BodyDeclaration<?> member) nestedMember(member);
        else visit(child, owned, separateLambdas);
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
