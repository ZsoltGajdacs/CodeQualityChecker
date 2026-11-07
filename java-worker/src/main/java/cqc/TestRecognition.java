package cqc;

import com.github.javaparser.ast.ImportDeclaration;
import com.github.javaparser.ast.body.CallableDeclaration;
import com.github.javaparser.resolution.declarations.ResolvedMethodDeclaration;
import java.util.*;

final class TestRecognition {
    private static final Set<String> PACKAGES = Set.of("org.junit.jupiter.api", "org.junit.jupiter.params");
    private static final Set<String> TESTS = Set.of("Test", "ParameterizedTest", "RepeatedTest", "TestFactory", "TestTemplate");
    private static final Set<String> ASSERTJ = Set.of("is", "has", "contains", "doesNot", "startsWith", "endsWith", "satisfies", "matches", "allMatch", "anyMatch", "noneMatch", "allSatisfy", "anySatisfy", "assertThatThrownBy");

    static boolean assertion(ResolvedMethodDeclaration method) {
        String owner = method.declaringType().getQualifiedName();
        String name = method.getName();
        if (owner.startsWith("org.junit.")) return junitAssertion(name);
        if (owner.startsWith("org.assertj.")) return ASSERTJ.stream().anyMatch(name::startsWith);
        if (owner.startsWith("org.hamcrest.")) return name.equals("assertThat");
        if (owner.startsWith("org.mockito.")) return mockAssertion(name);
        return false;
    }

    private static boolean junitAssertion(String name) { return name.startsWith("assert") || name.equals("fail"); }
    private static boolean mockAssertion(String name) { return name.equals("verify") || name.startsWith("verifyNo"); }

    static boolean annotation(CallableDeclaration<?> declaration, String name) {
        return declaration.getAnnotations().stream().anyMatch(a -> {
            String value = a.getNameAsString();
            if (PACKAGES.stream().anyMatch(p -> value.equals(p + "." + name))) return true;
            return value.equals(name) && imported(declaration, name);
        });
    }

    private static boolean imported(CallableDeclaration<?> declaration, String name) {
        return declaration.findCompilationUnit().map(unit -> unit.getImports().stream()
            .anyMatch(i -> matchesImport(i, name))).orElse(false);
    }

    private static boolean matchesImport(ImportDeclaration declaration, String name) {
        String value = declaration.getNameAsString();
        if (declaration.isAsterisk()) return PACKAGES.contains(value);
        return PACKAGES.stream().anyMatch(p -> value.equals(p + "." + name));
    }

    static boolean testAnnotation(CallableDeclaration<?> declaration) {
        return TESTS.stream().anyMatch(name -> annotation(declaration, name));
    }
}
