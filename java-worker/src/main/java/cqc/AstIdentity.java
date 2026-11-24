package cqc;
import com.github.javaparser.ast.*;
import com.github.javaparser.ast.body.*;
import com.github.javaparser.ast.expr.*;
import com.github.javaparser.resolution.declarations.ResolvedMethodDeclaration;
import com.github.javaparser.resolution.types.ResolvedType;
import java.util.*;
import java.util.stream.Collectors;

final class AstIdentity {
    static String typeName(TypeDeclaration<?> type) {
        var parts = new ArrayList<String>();
        Node current = type;
        while (current != null) {
            if (current instanceof TypeDeclaration<?> declaration) parts.add(declaration.getNameAsString());
            current = current.getParentNode().orElse(null);
        }
        Collections.reverse(parts);
        String name = String.join(".", parts);
        String namespace = type.findCompilationUnit().map(AstIdentity::packageName).orElse("");
        return namespace.isEmpty() ? name : namespace + "." + name;
    }

    static String packageName(CompilationUnit unit) {
        return unit.getPackageDeclaration().map(p -> p.getNameAsString()).orElse("");
    }

    static String callableId(CallableDeclaration<?> declaration) {
        String parameters = declaration.getParameters().stream().map(p -> {
            ResolvedType resolved = p.getType().resolve();
            return resolved.describe() + (p.isVarArgs() ? "[]" : "");
        }).collect(Collectors.joining(","));
        return owner(declaration).map(AstIdentity::typeName).orElseThrow() + "." +
            (declaration instanceof ConstructorDeclaration ? ".ctor" : declaration.getNameAsString()) + "(" + parameters + ")";
    }

    static String methodId(ResolvedMethodDeclaration declaration) {
        var parameters = new ArrayList<String>();
        for (int i = 0; i < declaration.getNumberOfParams(); i++) parameters.add(declaration.getParam(i).getType().describe());
        return declaration.declaringType().getQualifiedName() + "." + declaration.getName() + "(" + String.join(",", parameters) + ")";
    }

    static Optional<TypeDeclaration<?>> owner(Node node) {
        return node.getParentNode().flatMap(parent -> parent instanceof TypeDeclaration<?> declaration ? Optional.of(declaration) : owner(parent));
    }

    static int line(Node node) { return node.getBegin().map(p -> p.line).orElse(1); }
    static Worker.Location location(String file, Node node) { return new Worker.Location(file.replace('\\', '/'), line(node), node.getEnd().map(p -> p.line).orElse(line(node))); }

    static String sourceKey(String file, Node node) {
        var position = node.getBegin().orElseThrow();
        return "@" + file + ":" + position.line + ":" + position.column;
    }

    /** Subtree shapes preserve operators and method names but normalize locals and literals. */
    static String fingerprint(Node node, int depth, java.util.function.Predicate<Node> included) {
        String label = node.getClass().getSimpleName();
        if (node instanceof BinaryExpr expression) label += ":" + expression.getOperator().asString();
        if (node instanceof UnaryExpr expression) label += ":" + expression.getOperator().asString();
        if (node instanceof AssignExpr expression) label += ":" + expression.getOperator().asString();
        if (node instanceof MethodCallExpr expression) label += ":" + expression.getNameAsString();
        if (depth == 0) return label;
        var children = node.getChildNodes().stream().filter(included).filter(n -> !(n instanceof com.github.javaparser.ast.comments.Comment));
        return label + "(" + children.map(n -> fingerprint(n, depth - 1, included)).collect(Collectors.joining(",")) + ")";
    }

}
