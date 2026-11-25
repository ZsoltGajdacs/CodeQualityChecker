package cqc;

import com.github.javaparser.ast.body.*;
import com.github.javaparser.resolution.types.ResolvedType;
import java.util.*;
import static cqc.AstIdentity.*;

/** Produces a JVM coverage alias without changing the authored method identity. */
final class JvmCoverageSignature {
    static String of(CallableDeclaration<?> declaration) {
        try {
            var type = owner(declaration).orElseThrow();
            if (declaration instanceof ConstructorDeclaration)
                return signature(type, ".ctor", constructorPrefix(type), declaration.getParameters());
            return signature(type, declaration.getNameAsString(), List.of(), declaration.getParameters());
        } catch (RuntimeException exception) {
            return null;
        }
    }

    static String of(RecordDeclaration record) {
        try { return signature(record, ".ctor", List.of(), record.getParameters()); }
        catch (RuntimeException exception) { return null; }
    }

    private static String signature(TypeDeclaration<?> type, String method, List<String> prefix, Iterable<Parameter> parameters) {
        var erased = new ArrayList<>(prefix);
        for (var parameter : parameters)
            erased.add(erasedName(parameter.getType().resolve()) + (parameter.isVarArgs() ? "[]" : ""));
        return binaryTypeName(type) + "." + method + "(" + String.join(",", erased) + ")";
    }

    private static List<String> constructorPrefix(TypeDeclaration<?> type) {
        if (type.isEnumDeclaration()) return List.of("java.lang.String", "int");
        return enclosingInstance(type).map(enclosing -> List.of(binaryTypeName(enclosing))).orElseGet(List::of);
    }

    private static Optional<TypeDeclaration<?>> enclosingInstance(TypeDeclaration<?> type) {
        return type.toClassOrInterfaceDeclaration()
            .filter(ClassOrInterfaceDeclaration::isInnerClass)
            .flatMap(ClassOrInterfaceDeclaration::getParentNode)
            .filter(TypeDeclaration.class::isInstance)
            .<TypeDeclaration<?>>map(parent -> (TypeDeclaration<?>) parent)
            .filter(JvmCoverageSignature::permitsInnerClass);
    }

    private static boolean permitsInnerClass(TypeDeclaration<?> enclosing) {
        return !enclosing.isAnnotationDeclaration()
            && enclosing.toClassOrInterfaceDeclaration().filter(ClassOrInterfaceDeclaration::isInterface).isEmpty();
    }

    private static String erasedName(ResolvedType type) {
        if (type.isArray()) return erasedName(type.asArrayType().getComponentType()) + "[]";
        type = eraseVariables(type);
        if (!type.isReferenceType()) return type.erasure().describe();
        var declaration = type.asReferenceType().getTypeDeclaration().orElseThrow();
        String owner = declaration.getClassName().replace('.', '$');
        String namespace = declaration.getPackageName();
        return namespace.isEmpty() ? owner : namespace + "." + owner;
    }

    private static ResolvedType eraseVariables(ResolvedType type) {
        if (!type.isTypeVariable()) return type;
        var seen = new HashSet<ResolvedType>();
        while (type.isTypeVariable()) {
            if (!seen.add(type)) throw new IllegalArgumentException("Cyclic generic bound");
            type = type.erasure();
        }
        return type;
    }

    private JvmCoverageSignature() { }
}
