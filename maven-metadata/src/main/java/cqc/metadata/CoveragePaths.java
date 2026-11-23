package cqc.metadata;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.LinkOption;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/** Resolves paths in the isolated build and counts claims across the reactor. */
final class CoveragePaths {
    private final Path scratchRoot;
    private final Map<Path, Integer> claims = new HashMap<>();

    CoveragePaths(Path scratchRoot) throws IOException {
        this.scratchRoot = scratchRoot.toRealPath();
    }

    Path insideScratch(Path path) {
        try {
            var full = path.toAbsolutePath().normalize();
            var ancestor = full;
            while (!Files.exists(ancestor, LinkOption.NOFOLLOW_LINKS)) ancestor = ancestor.getParent();
            var canonical = ancestor.toRealPath().resolve(ancestor.relativize(full));
            return canonical.startsWith(scratchRoot) ? canonical : null;
        } catch (IOException inaccessible) {
            return null;
        }
    }

    void claim(Path path) {
        claims.merge(path, 1, Integer::sum);
    }

    boolean shared(Path path) {
        return claims.getOrDefault(path, 0) > 1;
    }
}
