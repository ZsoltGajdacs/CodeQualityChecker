package cqc;

import com.github.javaparser.ast.Node;
import com.github.javaparser.ast.body.CallableDeclaration;
import com.github.javaparser.ast.expr.Expression;
import com.github.javaparser.ast.stmt.Statement;
import java.util.Collections;
import java.util.IdentityHashMap;
import java.util.List;
import java.util.function.Function;

/** Normalized structural shapes used only by duplication detection. */
final class StructuralFingerprint {
    static String[] create(CallableDeclaration<?> declaration, List<Node> nodes) {
        if (ConstructorWiring.onlyAssignments(declaration)) return new String[0];
        return createOwned(nodes);
    }

    static String[] createOwned(List<Node> nodes) {
        var owned = Collections.newSetFromMap(new IdentityHashMap<Node, Boolean>());
        owned.addAll(nodes);
        return create(nodes, node -> AstIdentity.fingerprint(node, 3, owned::contains));
    }

    private static String[] create(List<Node> nodes, Function<Node, String> shape) {
        return nodes.stream().filter(n -> n instanceof Statement || n instanceof Expression)
            .map(shape).distinct().sorted().toArray(String[]::new);
    }
}
