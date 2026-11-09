package cqc;

import com.github.javaparser.ast.Node;
import com.github.javaparser.ast.body.CallableDeclaration;
import com.github.javaparser.ast.expr.*;
import com.github.javaparser.ast.stmt.*;
import java.util.*;
import static cqc.AstIdentity.*;

final class MethodMeasurement {
    private static final Set<Class<?>> DECISIONS = Set.of(IfStmt.class, ForStmt.class, ForEachStmt.class,
        WhileStmt.class, DoStmt.class, CatchClause.class, ConditionalExpr.class);
    private static final Set<BinaryExpr.Operator> LOGICAL = Set.of(BinaryExpr.Operator.AND, BinaryExpr.Operator.OR);
    private static final Set<Class<?>> NON_EXECUTABLE = Set.of(BlockStmt.class, EmptyStmt.class);
    record Metrics(int complexity, int lines, int nodes) { }

    static List<Node> nodes(CallableDeclaration<?> declaration, BlockStmt body) {
        return body.stream().filter(n -> belongs(n, declaration, body))
            .filter(n -> !(n instanceof com.github.javaparser.ast.comments.Comment)).toList();
    }

    static Metrics inspect(List<Node> nodes) {
        int complexity = 1 + nodes.stream().mapToInt(MethodMeasurement::decisions).sum();
        int lines = (int)nodes.stream().filter(MethodMeasurement::executable).map(AstIdentity::line).distinct().count();
        return new Metrics(complexity, lines, nodes.size());
    }

    private static boolean belongs(Node node, CallableDeclaration<?> declaration, BlockStmt body) {
        return node == body || callable(node).map(c -> c == declaration).orElse(false);
    }

    private static int decisions(Node node) {
        if (DECISIONS.contains(node.getClass())) return 1;
        if (node instanceof SwitchEntry entry) return entry.getLabels().size();
        if (node instanceof BinaryExpr binary) return LOGICAL.contains(binary.getOperator()) ? 1 : 0;
        return 0;
    }

    private static boolean executable(Node node) { return node instanceof Statement && !NON_EXECUTABLE.contains(node.getClass()); }
}
