package cqc;

import java.util.*;

/** Request-local source indexes; preserve authored source and resolver root ordering. */
final class ResolutionSources {
    private final Map<String, ContextManifest.Context> contexts = new HashMap<>();
    private final Map<String, List<Worker.Source>> sources = new HashMap<>();
    private record Root(int position, String path) { }
    private final Map<String, List<Root>> inferredRoots = new HashMap<>();

    ResolutionSources(List<ContextManifest.Context> definitions, List<Worker.Source> input) {
        for (var context : definitions) contexts.putIfAbsent(context.id(), context);
        for (int position = 0; position < input.size(); position++) {
            var source = input.get(position);
            String id = source.context().id();
            sources.computeIfAbsent(id, ignored -> new ArrayList<>()).add(source);
            inferredRoots.computeIfAbsent(id, ignored -> new ArrayList<>()).add(new Root(position, SourceContexts.sourceRoot(source)));
        }
    }

    Set<String> roots(Set<String> visible) {
        var result = new LinkedHashSet<String>();
        for (String id : visible) {
            var context = contexts.get(id);
            if (context != null) Collections.addAll(result, context.roots());
        }
        // Source ordering can affect resolver precedence, so keep global authored ordering.
        visible.stream().flatMap(id -> inferredRoots.getOrDefault(id, List.of()).stream())
            .sorted(Comparator.comparingInt(Root::position)).map(Root::path).forEach(result::add);
        return result;
    }

    List<Worker.Source> owned(String id) { return sources.getOrDefault(id, List.of()); }
}
