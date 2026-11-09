package cqc;

import com.github.javaparser.ast.body.TypeDeclaration;
import java.util.*;

/** Canonicalizes source declarations to their owning context, never the caller's module. */
final class SymbolContexts {
    private final Map<String, List<String>> owners = new HashMap<>();
    private final Map<String, ContextManifest.Context> contexts = new HashMap<>();

    SymbolContexts(List<Worker.Source> sources, List<ContextManifest.Context> definitions) {
        for (var context : definitions) contexts.put(context.id(), context);
        for (var source : sources)
            for (var type : source.unit().findAll(TypeDeclaration.class))
                owners.computeIfAbsent(AstIdentity.typeName(type), ignored -> new ArrayList<>()).add(source.context().id());
    }

    String type(String name, String caller) {
        var candidates = owners.getOrDefault(name, List.of());
        if (candidates.contains(caller)) return qualify(caller, name);
        var visible = visible(caller);
        var matches = candidates.stream().filter(visible::contains).distinct().toList();
        if (matches.size() > 1) throw new IllegalArgumentException("Ambiguous source declaration " + name + " in " + caller);
        return matches.isEmpty() ? name : qualify(matches.get(0), name);
    }

    String method(String name, String owner, String caller) {
        String type = type(owner, caller);
        return type.equals(owner) ? name : type + name.substring(owner.length());
    }

    Set<String> visible(String caller) {
        var seen = new LinkedHashSet<String>();
        var pending = new ArrayDeque<String>();
        pending.add(caller);
        while (!pending.isEmpty()) {
            String id = pending.removeFirst();
            if (!seen.add(id)) continue;
            var context = contexts.get(id);
            if (context != null) Collections.addAll(pending, context.reachable());
        }
        return seen;
    }

    static String qualify(String context, String symbol) { return context + "::" + symbol; }
}
