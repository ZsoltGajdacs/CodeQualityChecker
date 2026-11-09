package cqc;

import com.github.javaparser.ast.Node;
import com.github.javaparser.ast.body.CallableDeclaration;
import com.github.javaparser.ast.expr.Expression;
import com.github.javaparser.ast.stmt.Statement;
import java.util.List;

/** Normalized structural shapes used only by duplication detection. */
final class StructuralFingerprint {
    static String[] create(CallableDeclaration<?> declaration, List<Node> nodes) {
        if (ConstructorWiring.onlyAssignments(declaration)) return new String[0];
        return nodes.stream().filter(n -> n instanceof Statement || n instanceof Expression)
            .map(n -> AstIdentity.fingerprint(n, 3)).distinct().sorted().toArray(String[]::new);
    }
}
