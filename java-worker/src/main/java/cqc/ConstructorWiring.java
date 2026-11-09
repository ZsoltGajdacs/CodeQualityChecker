package cqc;

import com.github.javaparser.ast.body.*;
import com.github.javaparser.ast.expr.*;
import com.github.javaparser.ast.stmt.Statement;
import java.util.*;
import java.util.stream.Collectors;

/** Recognizes direct dependency wiring without resolving dependency types. */
final class ConstructorWiring {
    static boolean onlyAssignments(CallableDeclaration<?> declaration) {
        if (!(declaration instanceof ConstructorDeclaration constructor)) return false;
        if (constructor.getBody().getStatements().isEmpty()) return false;
        var parameters = constructor.getParameters().stream().map(Parameter::getNameAsString).toList();
        if (new HashSet<>(parameters).size() != parameters.size()) return false;
        var fields = fields(constructor);
        return constructor.getBody().getStatements().stream()
            .allMatch(statement -> directAssignment(statement, parameters, fields));
    }

    private static Set<String> fields(ConstructorDeclaration constructor) {
        var owner = AstIdentity.owner(constructor);
        if (owner.isEmpty()) return Set.of();
        var declarations = owner.get().getFields().stream()
            .flatMap(field -> field.getVariables().stream()).collect(Collectors.groupingBy(VariableDeclarator::getNameAsString));
        return declarations.entrySet().stream().filter(entry -> entry.getValue().size() == 1)
            .filter(entry -> !entry.getValue().get(0).findAncestor(FieldDeclaration.class).orElseThrow().isStatic())
            .map(Map.Entry::getKey).collect(Collectors.toSet());
    }

    private static boolean directAssignment(Statement statement, List<String> parameters, Set<String> fields) {
        if (!statement.isExpressionStmt()) return false;
        var expression = statement.asExpressionStmt().getExpression();
        if (!(expression instanceof AssignExpr assignment) || assignment.getOperator() != AssignExpr.Operator.ASSIGN) return false;
        if (!(assignment.getValue() instanceof NameExpr value) || !parameters.contains(value.getNameAsString())) return false;
        return targetField(assignment.getTarget(), parameters).map(fields::contains).orElse(false);
    }

    private static Optional<String> targetField(Expression target, List<String> parameters) {
        if (target instanceof NameExpr name && !parameters.contains(name.getNameAsString())) return Optional.of(name.getNameAsString());
        if (!(target instanceof FieldAccessExpr field)) return Optional.empty();
        if (!(field.getScope() instanceof ThisExpr scope) || scope.getTypeName().isPresent()) return Optional.empty();
        return Optional.of(field.getNameAsString());
    }
}
